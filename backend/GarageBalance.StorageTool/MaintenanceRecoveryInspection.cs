using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

public sealed record MaintenanceRecoveryState(Guid PlanId, bool ServiceWasActive, bool CommitVerified, bool ReceiptPresent, string? ReceiptFingerprint,
    MaintenanceParticipantState? Participants = null, bool ParticipantsPermitted = false, bool RollbackVerified = false, string? ReceiptOutcome = null);

// Inspection is deliberately read-only. Missing audit evidence does not prove rollback,
// and this helper never executes the cleanup or removes a marker.
public static class MaintenanceRecoveryInspection
{
    public static async Task<MaintenanceRecoveryState> InspectHostAsync(LinuxMaintenanceBarrierHost host, GarageBalanceDbContext db,
        SelectiveFinancialCleanupPlan plan, CancellationToken cancellationToken)
    {
        await using var deployGate = await host.AcquireDeployGateAsync(cancellationToken);
        return await InspectWithDeployGateHeldAsync(host, db, plan, cancellationToken);
    }

    public static async Task<MaintenanceRecoveryState> InspectWithDeployGateHeldAsync(LinuxMaintenanceBarrierHost host, GarageBalanceDbContext db,
        SelectiveFinancialCleanupPlan plan, CancellationToken cancellationToken)
    {
        var files = host.ReadRecoveryFiles();
        // Parse before database access; never broaden a malformed or mismatched target.
        var candidate = MaintenanceRecoveryStateParser.InspectIdentity(files.Marker, files.Receipt, files.ParticipantsPermitted);
        if (candidate.PlanId != plan.PlanId || plan.Fingerprint != SelectiveFinancialCleanupEngine.Fingerprint(plan))
            throw new InvalidOperationException("maintenance_recovery_plan_mismatch");
        if (candidate.ReceiptPresent && candidate.ReceiptFingerprint != plan.Fingerprint)
            throw new InvalidOperationException("maintenance_recovery_plan_mismatch");
        await StagingTargetBinding.VerifyAsync(db, cancellationToken);
        var id = plan.PlanId.ToString();
        var audits = await db.AuditEvents.AsNoTracking().CountAsync(e => e.Action == "finance.selective_history_cleared" && e.EntityId == id, cancellationToken);
        var state = Inspect(files.Marker, files.Receipt, audits, files.ParticipantsPermitted);
        if (state.CommitVerified)
        {
            var verifier = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
            if (files.Receipt is not null)
                await verifier.VerifyRecoveryAfterStartAsync(plan, cancellationToken);
            else
                await verifier.VerifyPostCommitAsync(plan, cancellationToken);
        }
        else if (state.RollbackVerified)
            await new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System)
                .VerifyRollbackRecoveryAfterStartAsync(plan, cancellationToken);
        return state;
    }

    public static MaintenanceRecoveryState Inspect(string? marker, string? receipt, int matchingCommittedAuditEvents, bool participantsPermitted = false) =>
        MaintenanceRecoveryStateParser.Inspect(marker, receipt, matchingCommittedAuditEvents, participantsPermitted);
}
