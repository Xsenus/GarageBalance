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
}
