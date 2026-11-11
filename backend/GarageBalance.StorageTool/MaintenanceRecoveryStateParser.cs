namespace GarageBalance.StorageTool;

public static class MaintenanceRecoveryStateParser
{
    public static MaintenanceRecoveryState Inspect(string? marker, string? receipt, int matchingCommittedAuditEvents, bool participantsPermitted = false)
        => InspectCore(marker, receipt, matchingCommittedAuditEvents, participantsPermitted, verifyOutcome: true);

    public static MaintenanceRecoveryState InspectIdentity(string? marker, string? receipt, bool participantsPermitted = false)
        => InspectCore(marker, receipt, 0, participantsPermitted, verifyOutcome: false);

    // Adopting durable file ownership is not proof of a financial outcome. Keep
    // both verification flags false until the caller has checked the database.
    public static MaintenanceRecoveryState InspectIdentityForAdoption(string? marker, string? receipt,
        bool participantsPermitted, MaintenanceRecoveryState expected, Guid planId, string planFingerprint)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var current = InspectIdentity(marker, receipt, participantsPermitted);
        if (expected.CommitVerified || expected.RollbackVerified ||
            current.PlanId != planId || current.PlanId != expected.PlanId ||
            current.ServiceWasActive != expected.ServiceWasActive || current.ReceiptPresent != expected.ReceiptPresent ||
            current.ReceiptFingerprint != expected.ReceiptFingerprint ||
            current.Participants?.Encode() != expected.Participants?.Encode() ||
            current.ParticipantsPermitted != expected.ParticipantsPermitted || current.ReceiptOutcome != expected.ReceiptOutcome ||
            current.ReceiptPresent && current.ReceiptFingerprint != planFingerprint)
            throw new InvalidOperationException("maintenance_recovery_state_mismatch");
        return current;
    }

    private static MaintenanceRecoveryState InspectCore(string? marker, string? receipt, int matchingCommittedAuditEvents, bool participantsPermitted, bool verifyOutcome)
    {
        if (matchingCommittedAuditEvents < 0 || matchingCommittedAuditEvents > 1)
            throw new InvalidOperationException("maintenance_recovery_audit_ambiguous");
        if (participantsPermitted && receipt is null) throw new InvalidOperationException("maintenance_recovery_state_invalid");
        var marked = marker is null ? null : Parse(marker, false);
        var receipted = receipt is null ? null : Parse(receipt, true);
        if (marked is null && receipted is null) throw new InvalidOperationException("maintenance_recovery_not_pending");
        if (marked is not null && receipted is not null && (marked.Value.PlanId != receipted.Value.PlanId || marked.Value.WasActive != receipted.Value.WasActive ||
            marked.Value.Participants?.Encode() != receipted.Value.Participants?.Encode()))
            throw new InvalidOperationException("maintenance_recovery_state_mismatch");
        var state = marked ?? receipted!;
        if (verifyOutcome && (receipted?.Outcome == "rolled-back" && matchingCommittedAuditEvents != 0 ||
            receipted?.Outcome == "committed" && matchingCommittedAuditEvents != 1))
            throw new InvalidOperationException("maintenance_recovery_outcome_mismatch");
        return new MaintenanceRecoveryState(state.Value.PlanId, state.Value.WasActive, matchingCommittedAuditEvents == 1, receipted is not null,
            receipted?.Fingerprint, state.Value.Participants, participantsPermitted, verifyOutcome && receipted?.Outcome == "rolled-back", receipted?.Outcome);
    }

    private static (Guid PlanId, bool WasActive, string? Fingerprint, MaintenanceParticipantState? Participants, string? Outcome)? Parse(string content, bool receipt)
    {
        var lines = content.Split('\n');
        var legacyLength = receipt ? 4 : 3;
        var versioned = receipt && lines.Length == 6;
        if ((!versioned && lines.Length != legacyLength && lines.Length != legacyLength + 1) || lines[^1] != "" || !Guid.TryParseExact(lines[0], "D", out var id) || id == Guid.Empty ||
            lines[1] is not ("active" or "inactive")) throw new InvalidOperationException("maintenance_recovery_state_invalid");
        if (receipt && (lines[2].Length != 64 || !lines[2].All(Uri.IsHexDigit))) throw new InvalidOperationException("maintenance_recovery_state_invalid");
        var participants = versioned || lines.Length == legacyLength + 1 ? MaintenanceParticipantState.Decode(lines[legacyLength - 1]) : null;
        var outcome = versioned ? lines[4] switch
        {
            "outcome=committed" => "committed",
            "outcome=rolled-back" => "rolled-back",
            _ => throw new InvalidOperationException("maintenance_recovery_outcome_invalid")
        } : null;
        return (id, lines[1] == "active", receipt ? lines[2] : null, participants, outcome);
    }
}
