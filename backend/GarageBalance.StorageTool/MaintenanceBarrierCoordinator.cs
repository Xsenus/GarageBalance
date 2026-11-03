namespace GarageBalance.StorageTool;

// The host adapter must provide durable markers and exclusive locks on the verified
// fixed inodes. The staging execution gate stays closed until that adapter is qualified.
public interface IMaintenanceBarrierHost
{
    ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken cancellationToken);
    Task CaptureServiceStateAsync(CancellationToken cancellationToken);
    Task CreateDurableMarkerAsync(CancellationToken cancellationToken);
    Task StopAndDrainAsync(CancellationToken cancellationToken);
    ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken cancellationToken);
    Task VerifyOfflineAsync(CancellationToken cancellationToken);
    Task VerifyCommittedAsync(CancellationToken cancellationToken);
    Task CreateRecoveryReceiptAsync(CancellationToken cancellationToken);
    Task RemoveDurableMarkerAsync(CancellationToken cancellationToken);
    Task RestoreServiceAsync(CancellationToken cancellationToken);
    Task PermitParticipantsAsync(CancellationToken cancellationToken);
    Task RestoreParticipantsAsync(CancellationToken cancellationToken);
    Task CompleteRecoveryAsync(CancellationToken cancellationToken);
}

public static class MaintenanceBarrierCoordinator
{
    // An uncertain COMMIT must be resolved by read-only recovery before restart.
    // Filesystem errors while removing a marker require separate recovery handling.
    public static async Task ExecuteAsync(IMaintenanceBarrierHost host, Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
        => await ExecuteCoreAsync(host, operation, null, null, cancellationToken);

    public static async Task ExecutePreparedAsync(IMaintenanceBarrierHost host, Func<CancellationToken, Task> adoptPrepared,
        Func<CancellationToken, Task> recordExecuting, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adoptPrepared);
        ArgumentNullException.ThrowIfNull(recordExecuting);
        await ExecuteCoreAsync(host, operation, adoptPrepared, recordExecuting, cancellationToken);
    }

    private static async Task ExecuteCoreAsync(IMaintenanceBarrierHost host, Func<CancellationToken, Task> operation,
        Func<CancellationToken, Task>? adoptPrepared, Func<CancellationToken, Task>? recordExecuting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(operation);
        await using var deploy = await host.AcquireDeployGateAsync(cancellationToken);
        if (adoptPrepared is null)
        {
            await host.CaptureServiceStateAsync(cancellationToken);
            await host.CreateDurableMarkerAsync(cancellationToken);
        }
        else await adoptPrepared(cancellationToken);
        await host.StopAndDrainAsync(cancellationToken);
        await using (var activity = await host.AcquireActivityGateAsync(cancellationToken))
        {
            await host.VerifyOfflineAsync(cancellationToken);
            if (recordExecuting is not null) await recordExecuting(cancellationToken);
            await operation(cancellationToken);
            await host.VerifyCommittedAsync(cancellationToken);
            await host.CreateRecoveryReceiptAsync(cancellationToken);
            await host.RemoveDurableMarkerAsync(cancellationToken);
        }
        // The service takes the shared activity gate when starting. Never wait for
        // it while holding that gate exclusively; deploy remains excluded here.
        await host.RestoreServiceAsync(cancellationToken);
        await host.PermitParticipantsAsync(cancellationToken);
        await host.RestoreParticipantsAsync(cancellationToken);
        await host.CompleteRecoveryAsync(cancellationToken);
    }
}
