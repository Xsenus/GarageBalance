namespace GarageBalance.StorageTool;

public static class StagingMaintenancePreparation
{
    // The plan is captured AFTER timers, running sync and the API are drained.
    // Keep the durable marker when returning: backup and the subsequent execute
    // use this exact offline snapshot rather than a potentially stale live plan.
    // No cleanup callback exists in this preparation phase.
    public static async Task<SelectiveFinancialCleanupPlan> PrepareAsync(IMaintenanceBarrierHost host, Guid operationId,
        Func<Guid, CancellationToken, Task<SelectiveFinancialCleanupPlan>> preview,
        Func<SelectiveFinancialCleanupPlan, CancellationToken, Task> persist,
        CancellationToken token, Func<CancellationToken, Task>? recordPreparing = null,
        Func<SelectiveFinancialCleanupPlan, CancellationToken, Task>? recordPrepared = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(persist);
        if (operationId == Guid.Empty) throw new ArgumentException("An operation ID is required.", nameof(operationId));
        await using var deploy = await host.AcquireDeployGateAsync(token);
        await host.CaptureServiceStateAsync(token);
        await host.CreateDurableMarkerAsync(token);
        if (recordPreparing is not null) await recordPreparing(token);
        await host.StopAndDrainAsync(token);
        await using var activity = await host.AcquireActivityGateAsync(token);
        await host.VerifyOfflineAsync(token);
        var plan = await preview(operationId, token);
        if (plan.PlanId != operationId || plan.FormatVersion != 2 || plan.HeldAnnualYear != 2026 ||
            plan.Fingerprint != SelectiveFinancialCleanupEngine.Fingerprint(plan))
            throw new InvalidOperationException("maintenance_prepared_plan_invalid");
        await persist(plan, token);
        if (recordPrepared is not null) await recordPrepared(plan, token);
        await host.VerifyOfflineAsync(token);
        return plan;
    }
}
