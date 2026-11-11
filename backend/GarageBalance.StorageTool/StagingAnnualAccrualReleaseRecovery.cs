using GarageBalance.Api.Infrastructure.Data;

namespace GarageBalance.StorageTool;

// Recovery cannot call ReleaseAsync. Even a preparing/missing journal needs exact
// original DB state; an executing journal is never eligible for an execution retry.
public static class StagingAnnualAccrualReleaseRecovery
{
    public static async Task<string> ResumeAsync(LinuxMaintenanceBarrierHost host, StagingServiceLifecycle lifecycle,
        GarageBalanceDbContext db, AnnualAccrualHoldReleasePlan plan,
        Action<MaintenanceRecoveryState> setRecordedState, CancellationToken token)
    {
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        ArgumentNullException.ThrowIfNull(setRecordedState);
        await using var deploy = await host.AcquireDeployGateAsync(token);
        MaintenanceRecoveryState initial;
        MaintenancePreparationProgress? progress;
        (string? Marker, string? Receipt, bool ParticipantsPermitted) files;
        try
        {
            files = host.ReadRecoveryFiles();
            initial = MaintenanceRecoveryStateParser.InspectIdentity(files.Marker, files.Receipt, files.ParticipantsPermitted);
            progress = host.ReadPreparationProgress();
            ValidateIdentity(initial, progress, plan);
            setRecordedState(initial);
            host.AdoptRecoveryIdentity(initial);
            // Once pending evidence is adopted, installing the startup block is a
            // required safety step even if the caller has just canceled recovery.
            if (files.Marker is null) await host.CreateDurableMarkerAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            // Absence of any pending state remains an error, not a new operation.
            // For malformed existing evidence, conservatively retain a startup block.
            if (error.Message != "maintenance_recovery_not_pending")
                await host.PreserveUnresolvedRecoveryMarkerAsync(CancellationToken.None);
            throw;
        }
        await lifecycle.CaptureStateAsync(token);
        await lifecycle.StopAndDrainAsync(token);
        AnnualAccrualReleaseOutcome outcome;
        await using (var activity = await host.AcquireActivityGateAsync(token))
        {
            await lifecycle.VerifyOfflineAsync(token);
            var fresh = host.ReadRecoveryFiles();
            var state = MaintenanceRecoveryStateParser.InspectIdentity(fresh.Marker, fresh.Receipt, fresh.ParticipantsPermitted);
            var currentProgress = host.ReadPreparationProgress();
            ValidateIdentity(state, currentProgress, plan);
            if (state != initial || currentProgress != progress)
            {
                // A newly reinstalled marker does not change the logical state.
                if (state.PlanId != initial.PlanId || state.ServiceWasActive != initial.ServiceWasActive ||
                    state.ReceiptPresent != initial.ReceiptPresent || state.ReceiptFingerprint != initial.ReceiptFingerprint ||
                    state.Participants?.Encode() != initial.Participants?.Encode() || state.ParticipantsPermitted != initial.ParticipantsPermitted ||
                    state.ReceiptOutcome != initial.ReceiptOutcome || currentProgress != progress)
                    throw new InvalidOperationException("maintenance_recovery_state_mismatch");
            }
            var verifier = new AnnualAccrualHoldReleaseEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                confirmStopped: async ct => { await lifecycle.VerifyOfflineAsync(ct); return true; });
            outcome = await verifier.VerifyOutcomeAsync(plan, token);
            if (outcome == AnnualAccrualReleaseOutcome.Committed && progress?.Phase != "executing")
                throw new InvalidOperationException("maintenance_release_execution_unproven");
            var checkedState = MaintenanceRecoveryStateParser.Inspect(fresh.Marker, fresh.Receipt,
                outcome == AnnualAccrualReleaseOutcome.Committed ? 1 : 0, fresh.ParticipantsPermitted);
            if (checkedState.ReceiptPresent && checkedState.ReceiptOutcome is not ("committed" or "rolled-back"))
                throw new InvalidOperationException("maintenance_release_receipt_unproven");
            if (!checkedState.ReceiptPresent)
            {
                if (outcome == AnnualAccrualReleaseOutcome.Committed) await host.CreateRecoveryReceiptAsync(token);
                else await host.CreateRollbackRecoveryReceiptAsync(token);
            }
            await host.RemoveDurableMarkerAsync(token);
        }
        await lifecycle.RestoreRecordedAsync(initial.ServiceWasActive, token, initial.Participants);
        await host.PermitParticipantsAsync(token);
        await lifecycle.RestoreParticipantsAsync(token);
        await host.CompleteAnnualReleaseRecoveryAsync(token);
        return outcome == AnnualAccrualReleaseOutcome.Committed ? "committed" : "rolled-back";
    }

    public static void ValidateIdentity(MaintenanceRecoveryState state, MaintenancePreparationProgress? progress,
        AnnualAccrualHoldReleasePlan plan)
    {
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        if (progress is not null) _ = progress.Encode();
        if (state.PlanId != plan.PlanId || state.Participants is null ||
            state.ReceiptPresent && state.ReceiptFingerprint != plan.Fingerprint ||
            progress is not null && (progress.PlanId != plan.PlanId || progress.Phase != "preparing" && progress.Fingerprint != plan.Fingerprint) ||
            state.ReceiptPresent && state.ReceiptOutcome is not ("committed" or "rolled-back"))
            throw new InvalidOperationException("maintenance_recovery_plan_mismatch");
        state.Participants.Validate();
    }
}
