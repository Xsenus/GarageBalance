namespace GarageBalance.StorageTool;

// This fixture uses the real pure state parser and host adoption, but never the
// database inspection or the working service recovery runner.
public sealed record MaintenanceRecoveryState(Guid PlanId, bool ServiceWasActive, bool CommitVerified, bool ReceiptPresent, string? ReceiptFingerprint,
    MaintenanceParticipantState? Participants = null, bool ParticipantsPermitted = false, bool RollbackVerified = false, string? ReceiptOutcome = null);
public static class MaintenanceRecoveryInspection
{
    public static MaintenanceRecoveryState Inspect(string? marker, string? receipt, int matchingCommittedAuditEvents, bool participantsPermitted = false) =>
        MaintenanceRecoveryStateParser.Inspect(marker, receipt, matchingCommittedAuditEvents, participantsPermitted);
}
