using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.StorageTool;

// The only production entrypoint for release. It never accepts an injected host,
// identity, authority factory, or arbitrary database/service target.
public static class StagingAnnualAccrualReleaseRunner
{
    public static async Task ExecuteAsync(string connection, AnnualAccrualHoldReleasePlan plan, string confirmation,
        AccountingCleanupBackupReceipt backup, string reason, IBusinessDateProvider businessDate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(businessDate);
        RequireConnection(connection);
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        if (confirmation != plan.Fingerprint) throw new AccountingCleanupException("cleanup_plan_invalid", "Exact release confirmation is required.");
        await RequireQualifiedAsync(token);
        var lifecycle = new StagingServiceLifecycle();
        using var barrier = new LinuxMaintenanceBarrierHost(plan.PlanId, plan.Fingerprint,
            lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync,
            async ct =>
            {
                await using var verification = Context(connection);
                var verifier = new AnnualAccrualHoldReleaseEngine(verification, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                    confirmStopped: async inner => { await lifecycle.VerifyOfflineAsync(inner); return true; });
                if (await verifier.VerifyOutcomeAsync(plan, ct) != AnnualAccrualReleaseOutcome.Committed)
                    throw new InvalidOperationException("maintenance_release_commit_unverified");
            }, lifecycle.RestoreAsync, () => lifecycle.ServiceWasActive, () => lifecycle.ParticipantState,
            lifecycle.RestoreParticipantsAsync);
        await AnnualAccrualReleaseProtocol.ExecuteAsync(barrier,
            (phase, ct) => phase == "verify-fresh" ? barrier.RequireFreshAnnualReleaseAsync(ct)
                : barrier.TransitionPreparationAsync(phase, phase == "preparing" ? null : plan.Fingerprint, ct),
            async ct =>
            {
                var permit = await AnnualAccrualReleasePermit.CreateAsync(barrier, plan, ct);
                var identity = await new StagingOfflineProof().GetIdentityAsync(StagingOfflineProof.Service, null, ct);
                await using var db = Context(connection);
                var engine = new AnnualAccrualHoldReleaseEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                    confirmStopped: async inner => { await lifecycle.VerifyOfflineAsync(inner); return true; },
                    stagingPermit: permit, businessDate: businessDate);
                await engine.ReleaseAsync(plan, confirmation, backup, identity, reason, ct);
            }, token);
    }

    public static async Task<string> RecoverAsync(string connection, AnnualAccrualHoldReleasePlan plan, CancellationToken token)
    {
        RequireConnection(connection);
        AnnualAccrualHoldReleaseEngine.ValidatePlan(plan);
        await RequireQualifiedAsync(token);
        var lifecycle = new StagingServiceLifecycle(allowFailedForRecovery: true);
        MaintenanceRecoveryState? recorded = null;
        using var barrier = new LinuxMaintenanceBarrierHost(plan.PlanId, plan.Fingerprint,
            lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync,
            _ => throw new InvalidOperationException("maintenance_recovery_cannot_execute"),
            lifecycle.RestoreAsync, () => recorded?.ServiceWasActive ?? lifecycle.ServiceWasActive,
            () => recorded?.Participants ?? lifecycle.ParticipantState, lifecycle.RestoreParticipantsAsync);
        await using var db = Context(connection);
        return await StagingAnnualAccrualReleaseRecovery.ResumeAsync(barrier, lifecycle, db, plan, state => recorded = state, token);
    }

    private static GarageBalanceDbContext Context(string connection) => new(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(connection).Options);

    private static void RequireConnection(string connection)
    {
        if (string.IsNullOrWhiteSpace(connection) || new NpgsqlConnectionStringBuilder(connection).Database != "garagebalance_staging")
            throw new AccountingCleanupException("cleanup_database_mismatch", "Only the exact staging database is allowed.");
    }

    private static async Task RequireQualifiedAsync(CancellationToken token)
    {
        try { await StagingBarrierQualification.VerifyInstalledAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { throw new AccountingCleanupException("cleanup_service_exclusion_unverified", "Installed staging maintenance barrier is not qualified."); }
    }
}
