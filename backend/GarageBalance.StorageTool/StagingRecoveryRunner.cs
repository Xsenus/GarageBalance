using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.StorageTool;

// Recovery has no cleanup callback and cannot repeat financial deletion.
// The CLI gate remains closed until the complete host protocol is qualified.
public static class StagingRecoveryRunner
{
    // Preparing cannot have executed financial SQL: execute requires the durable
    // prepared fingerprint and must atomically advance it to executing first.
    // This recovery path deliberately has no database or cleanup callback.
    public static async Task AbortPreparationAsync(LinuxMaintenanceBarrierHost host, StagingServiceLifecycle lifecycle,
        Action<MaintenanceRecoveryState> setRecordedServiceState, CancellationToken token)
    {
        await using var deploy = await host.AcquireDeployGateAsync(token);
        if (host.ReadPreparationProgress() is not { Phase: "preparing", Fingerprint: null })
            throw new InvalidOperationException("maintenance_preparation_abort_unproven");
        var files = host.ReadRecoveryFiles();
        var state = MaintenanceRecoveryInspection.Inspect(files.Marker, files.Receipt, 0, files.ParticipantsPermitted);
        if (state.Participants is null || state.ReceiptPresent && (!state.RollbackVerified || state.ReceiptFingerprint != new string('0', 64)))
            throw new InvalidOperationException("maintenance_preparation_abort_unproven");
        setRecordedServiceState(state);
        host.AdoptRecoveryFiles(state);
        if (files.Marker is null) await host.CreateDurableMarkerAsync(token);
        await lifecycle.CaptureStateAsync(token);
        await lifecycle.StopAndDrainAsync(token);
        await using (var activity = await host.AcquireActivityGateAsync(token))
        {
            await lifecycle.VerifyOfflineAsync(token);
            if (host.ReadPreparationProgress() is not { Phase: "preparing", Fingerprint: null })
                throw new InvalidOperationException("maintenance_preparation_abort_unproven");
            if (!state.ReceiptPresent) await host.CreateRollbackRecoveryReceiptAsync(token);
            await host.RemoveDurableMarkerAsync(token);
        }
        await lifecycle.RestoreRecordedAsync(state.ServiceWasActive, token, state.Participants);
        await host.PermitParticipantsAsync(token);
        await lifecycle.RestoreParticipantsAsync(token);
        await host.CompleteRecoveryAsync(token);
    }

    public static async Task<string> ResumeAsync(LinuxMaintenanceBarrierHost host, StagingServiceLifecycle lifecycle,
        GarageBalanceDbContext db, SelectiveFinancialCleanupPlan plan, Action<MaintenanceRecoveryState> setRecordedServiceState, CancellationToken token)
    {
        await using var deploy = await host.AcquireDeployGateAsync(token);
        var initialFiles = host.ReadRecoveryFiles();
        var initial = await MaintenanceRecoveryInspection.InspectWithDeployGateHeldAsync(host, db, plan, token);
        if (initial.Participants is null) throw new InvalidOperationException("maintenance_participant_state_missing");
        if (initial.ReceiptPresent && !initial.CommitVerified && !initial.RollbackVerified)
            throw new InvalidOperationException("maintenance_commit_unknown");
        setRecordedServiceState(initial);
        host.AdoptRecoveryFiles(initial);
        // A receipt without a marker permits app startup. Install the marker under
        // the deploy gate before stopping/draining; never modify the database here.
        if (initialFiles.Marker is null) await host.CreateDurableMarkerAsync(token);
        await lifecycle.CaptureStateAsync(token);
        await lifecycle.StopAndDrainAsync(token);
        await using (var activity = await host.AcquireActivityGateAsync(token))
        {
            await lifecycle.VerifyOfflineAsync(token);
            var freshFiles = host.ReadRecoveryFiles();
            var current = MaintenanceRecoveryInspection.Inspect(freshFiles.Marker, freshFiles.Receipt, initial.CommitVerified ? 1 : 0, freshFiles.ParticipantsPermitted);
            if (current.PlanId != initial.PlanId || current.ServiceWasActive != initial.ServiceWasActive ||
                current.ReceiptPresent != initial.ReceiptPresent ||
                current.Participants?.Encode() != initial.Participants.Encode() ||
                current.ParticipantsPermitted != initial.ParticipantsPermitted ||
                current.ReceiptOutcome != initial.ReceiptOutcome ||
                (current.ReceiptPresent && current.ReceiptFingerprint != plan.Fingerprint))
                throw new InvalidOperationException("maintenance_recovery_state_mismatch");
            var verifier = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                confirmStopped: async ct => { await lifecycle.VerifyOfflineAsync(ct); return true; });
            if (initial.CommitVerified)
            {
                if (initial.ReceiptPresent) await verifier.VerifyRecoveryAfterStartAsync(plan, token);
                else await verifier.VerifyPostCommitAsync(plan, token);
                if (!current.ReceiptPresent) await host.CreateRecoveryReceiptAsync(token);
            }
            else
            {
                if (initial.RollbackVerified) await verifier.VerifyRollbackRecoveryAfterStartAsync(plan, token);
                else await verifier.VerifyRolledBackAsync(plan, token);
                if (!current.ReceiptPresent) await host.CreateRollbackRecoveryReceiptAsync(token);
            }
            await host.RemoveDurableMarkerAsync(token);
        }
        await lifecycle.RestoreRecordedAsync(initial.ServiceWasActive, token, initial.Participants);
        await host.PermitParticipantsAsync(token);
        await lifecycle.RestoreParticipantsAsync(token);
        await host.CompleteRecoveryAsync(token);
        return initial.CommitVerified ? "committed" : "rolled-back";
    }
}
