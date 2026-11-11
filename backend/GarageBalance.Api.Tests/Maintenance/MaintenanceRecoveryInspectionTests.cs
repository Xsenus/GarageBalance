using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class MaintenanceRecoveryInspectionTests
{
    private static readonly Guid Id = Guid.Parse("5aa5e64f-d46d-4af5-85a0-2cc016219477");
    private static readonly string State = $"{Id:D}\nactive\n";
    private static readonly string Receipt = $"{Id:D}\nactive\n{new string('a', 64)}\n";

    [Fact]
    public void MissingAuditNeverClaimsRollback() => Assert.False(MaintenanceRecoveryInspection.Inspect(State, null, 0).CommitVerified);

    [Fact]
    public void MatchingAuditAndReceiptIdentifyCommittedPlan()
    {
        var state = MaintenanceRecoveryInspection.Inspect(State, Receipt, 1);
        Assert.Equal(Id, state.PlanId);
        Assert.True(state.CommitVerified && state.ServiceWasActive && state.ReceiptPresent);
        Assert.Equal(new string('a', 64), state.ReceiptFingerprint);
    }

    [Fact]
    public void ExtendedStatePreservesParticipantsAndRejectsMismatchedOrMissingSnapshot()
    {
        var participants = new MaintenanceParticipantState(true, false, ["garagebalance-storage-tool@delta-sync.service"]);
        var marker = State + participants.Encode() + "\n";
        var receipt = Receipt + participants.Encode() + "\n";
        var state = MaintenanceRecoveryInspection.Inspect(marker, receipt, 1);
        Assert.Equal(participants.Encode(), state.Participants!.Encode());
        Assert.True(state.CommitVerified);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(marker, Receipt, 1));
        var different = new MaintenanceParticipantState(false, false, []);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(marker, Receipt + different.Encode() + "\n", 1));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(State + "broken\n", null, 0));
    }

    [Fact]
    public void ParticipantResumePhaseKeepsReceiptAndNeverFabricatesCommitEvidence()
    {
        var participant = new MaintenanceParticipantState(true, true, []);
        var receipt = Receipt + participant.Encode() + "\n";
        var state = MaintenanceRecoveryInspection.Inspect(null, receipt, 1, participantsPermitted: true);
        Assert.True(state.ParticipantsPermitted && state.ReceiptPresent && state.CommitVerified);
        Assert.Equal(participant.Encode(), state.Participants!.Encode());
        Assert.False(MaintenanceRecoveryInspection.Inspect(null, receipt, 0, participantsPermitted: true).CommitVerified);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(State, null, 0, participantsPermitted: true));
    }

    [Theory]
    [InlineData("broken", null, 0)]
    [InlineData(null, null, 0)]
    [InlineData(null, "5aa5e64f-d46d-4af5-85a0-2cc016219477\nactive\n", 1)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477\nactive\n", "5aa5e64f-d46d-4af5-85a0-2cc016219477\ninactive\n", 1)]
    [InlineData("5aa5e64f-d46d-4af5-85a0-2cc016219477\nactive\n", null, 2)]
    public void InvalidOrAmbiguousEvidenceFailsClosed(string? marker, string? receipt, int audits) =>
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(marker, receipt, audits));

    [Fact]
    public void TypedReceiptDistinguishesPositiveRollbackProofFromAbsenceOfAudit()
    {
        var participant = new MaintenanceParticipantState(true, true, []);
        var rolledBack = Receipt + participant.Encode() + "\noutcome=rolled-back\n";
        var committed = Receipt + participant.Encode() + "\noutcome=committed\n";
        var rollback = MaintenanceRecoveryInspection.Inspect(null, rolledBack, 0);
        Assert.True(rollback.RollbackVerified && rollback.ReceiptPresent);
        Assert.False(rollback.CommitVerified);
        Assert.Equal("rolled-back", rollback.ReceiptOutcome);
        Assert.False(MaintenanceRecoveryInspection.Inspect(State, null, 0).RollbackVerified);
        Assert.False(MaintenanceRecoveryInspection.Inspect(null, Receipt + participant.Encode() + "\n", 0).RollbackVerified);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(null, rolledBack, 1));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(null, committed, 0));
        Assert.True(MaintenanceRecoveryInspection.Inspect(null, committed, 1).CommitVerified);
        var identity = MaintenanceRecoveryStateParser.InspectIdentity(null, committed);
        Assert.Equal(Id, identity.PlanId);
        Assert.False(identity.CommitVerified || identity.RollbackVerified);
        Assert.Equal("committed", identity.ReceiptOutcome);
        Assert.False(MaintenanceRecoveryStateParser.InspectIdentity(null, rolledBack).RollbackVerified);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(null, Receipt + participant.Encode() + "\noutcome=unknown\n", 0));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryInspection.Inspect(null, Receipt + participant.Encode() + "\noutcome=rolled-back\nextra\n", 0));
    }

    [Theory]
    [InlineData("committed", false, false)]
    [InlineData("committed", true, false)]
    [InlineData("committed", false, true)]
    [InlineData("rolled-back", false, false)]
    [InlineData("rolled-back", true, false)]
    [InlineData("rolled-back", false, true)]
    public void IdentityAdoptionAcceptsTypedReceiptsWithoutClaimingAnOutcome(string outcome, bool markerPresent, bool participantsPermitted)
    {
        var participants = new MaintenanceParticipantState(true, false, ["garagebalance-storage-tool@delta-sync.service"]);
        var marker = markerPresent ? State + participants.Encode() + "\n" : null;
        var receipt = Receipt + participants.Encode() + $"\noutcome={outcome}\n";
        var identity = MaintenanceRecoveryStateParser.InspectIdentity(marker, receipt, participantsPermitted);

        var adopted = MaintenanceRecoveryStateParser.InspectIdentityForAdoption(marker, receipt, participantsPermitted,
            identity, Id, new string('a', 64));

        Assert.False(adopted.CommitVerified || adopted.RollbackVerified);
        Assert.Equal(identity with { Participants = adopted.Participants }, adopted);
        Assert.Equal(participants.Encode(), adopted.Participants!.Encode());
        Assert.Equal(outcome, adopted.ReceiptOutcome);
        // A claimed file outcome must still match the independent database proof.
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.Inspect(marker, receipt,
            outcome == "committed" ? 0 : 1, participantsPermitted));
        var verified = MaintenanceRecoveryStateParser.Inspect(marker, receipt, outcome == "committed" ? 1 : 0, participantsPermitted);
        Assert.Equal(outcome == "committed", verified.CommitVerified);
        Assert.Equal(outcome == "rolled-back", verified.RollbackVerified);
    }

    [Fact]
    public void IdentityAdoptionAcceptsMarkerOnlyButRejectsChangedOrAlreadyVerifiedEvidence()
    {
        var participants = new MaintenanceParticipantState(true, false, []);
        var marker = State + participants.Encode() + "\n";
        var identity = MaintenanceRecoveryStateParser.InspectIdentity(marker, null);
        var adopted = MaintenanceRecoveryStateParser.InspectIdentityForAdoption(marker, null, false, identity, Id, new string('a', 64));
        Assert.False(adopted.ReceiptPresent || adopted.CommitVerified || adopted.RollbackVerified);

        var receipt = Receipt + participants.Encode() + "\noutcome=committed\n";
        var receipted = MaintenanceRecoveryStateParser.InspectIdentity(null, receipt);
        var differentParticipants = new MaintenanceParticipantState(false, false, []);
        foreach (var invalid in new[] { receipted with { PlanId = Guid.NewGuid() }, receipted with { ServiceWasActive = false },
            receipted with { ReceiptPresent = false }, receipted with { ReceiptFingerprint = new string('b', 64) },
            receipted with { Participants = differentParticipants }, receipted with { Participants = null },
            receipted with { ParticipantsPermitted = true }, receipted with { ReceiptOutcome = "rolled-back" },
            receipted with { CommitVerified = true }, receipted with { RollbackVerified = true } })
            Assert.Equal("maintenance_recovery_state_mismatch", Assert.Throws<InvalidOperationException>(() =>
                MaintenanceRecoveryStateParser.InspectIdentityForAdoption(null, receipt, false, invalid, Id, new string('a', 64))).Message);
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
            null, receipt, false, receipted, Guid.NewGuid(), new string('a', 64)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
            null, receipt, false, receipted, Id, new string('b', 64)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
            marker, receipt, false, identity, Id, new string('a', 64)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
            marker, null, false, receipted, Id, new string('a', 64)));
    }

    [Fact]
    public void IdentityAdoptionStillRejectsMalformedMissingOrContradictoryFiles()
    {
        var participants = new MaintenanceParticipantState(true, true, []);
        var marker = State + participants.Encode() + "\n";
        var receipt = Receipt + participants.Encode() + "\noutcome=committed\n";
        var identity = MaintenanceRecoveryStateParser.InspectIdentity(marker, receipt);
        foreach (var invalid in new (string? Marker, string? Receipt, bool Permitted)[]
        {
            (null, null, false), ("broken", receipt, false), (marker, "broken", false),
            (marker.Replace("active", "inactive", StringComparison.Ordinal), receipt, false),
            (marker, receipt.Replace("committed", "unknown", StringComparison.Ordinal), false),
            (marker, null, true)
        })
            Assert.Throws<InvalidOperationException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
                invalid.Marker, invalid.Receipt, invalid.Permitted, identity, Id, new string('a', 64)));
        Assert.Throws<ArgumentNullException>(() => MaintenanceRecoveryStateParser.InspectIdentityForAdoption(
            marker, receipt, false, null!, Id, new string('a', 64)));
    }
}
