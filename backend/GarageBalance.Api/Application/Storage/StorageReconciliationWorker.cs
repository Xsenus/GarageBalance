using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.Api.Application.Storage;

public sealed class StorageReconciliationRunner(
    IStorageCatalog catalog,
    IStorageProviderRegistry providers,
    StorageConfigurationResolver resolver,
    StorageOperationHealthTracker health,
    TimeProvider timeProvider,
    ILogger<StorageReconciliationRunner> logger)
{
    private const int MaximumAnomaliesPerRun = 10;
    private readonly EffectiveStorageConfiguration configuration = resolver.Resolve();

    public async Task<int> ReconcileOnceAsync(CancellationToken cancellationToken)
        => await ReconcileAsync(null, cancellationToken);

    public async Task<int> ReconcileObjectAsync(string logicalKey, CancellationToken cancellationToken)
        => await ReconcileAsync(StorageObjectKey.Normalize(logicalKey), cancellationToken);

    private async Task<int> ReconcileAsync(string? logicalKey, CancellationToken cancellationToken)
    {
        if (configuration.Mode != StorageMode.AsyncMirror)
        {
            return 0;
        }
        var repaired = 0;
        var anomalies = 0;
        foreach (var dataClass in configuration.Policies.Select(item => item.DataClass).Distinct())
        {
            var policy = configuration.Policies.Single(item => item.DataClass == dataClass);
            var manifest = await catalog.ExportManifestAsync(dataClass, 100, cancellationToken);
            foreach (var storageObject in manifest.Where(item =>
                         item.State is not (StorageObjectState.Deleting or StorageObjectState.Deleted) &&
                         (logicalKey is null || string.Equals(item.LogicalKey, logicalKey, StringComparison.Ordinal))))
            {
                foreach (var replica in storageObject.Replicas.Where(item => item.State == StorageReplicaState.Available))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = configuration.Destinations.SingleOrDefault(item =>
                        string.Equals(item.Id, replica.DestinationId, StringComparison.Ordinal));
                    if (destination is null || destination.State == StorageDestinationState.Disabled ||
                        !destination.Capabilities.HasFlag(StorageCapability.Stat) ||
                        !health.TryBeginAttempt(destination.Id, StorageOperationKind.Verify))
                    {
                        continue;
                    }
                    try
                    {
                        var stat = await providers.GetRequired(destination.Id).StatAsync(replica.NativeLocator, cancellationToken);
                        health.RecordSuccess(destination.Id, StorageOperationKind.Verify);
                        var observedState = stat is null
                            ? StorageReplicaState.Missing
                            : stat.SizeBytes != storageObject.SizeBytes ||
                              !string.Equals(stat.ProviderChecksum, storageObject.Sha256, StringComparison.OrdinalIgnoreCase)
                                ? StorageReplicaState.Corrupted
                                : StorageReplicaState.Available;
                        if (observedState == StorageReplicaState.Available)
                        {
                            continue;
                        }
                        anomalies++;
                        if (anomalies > MaximumAnomaliesPerRun)
                        {
                            logger.LogCritical("Storage reconciliation stopped after the anomaly safety threshold was reached.");
                            return repaired;
                        }
                        if (await catalog.ScheduleRepairAsync(
                            storageObject.ObjectId,
                            destination.Id,
                            observedState,
                            observedState.ToString(),
                            "Storage reconciliation detected a missing or corrupt replica.",
                            policy.RequiredIndependentCopies,
                            policy.DesiredCopies,
                            configuration.Replication.MaximumAttempts,
                            timeProvider.GetUtcNow(),
                            cancellationToken))
                        {
                            repaired++;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (StorageProviderException exception)
                    {
                        health.RecordFailure(destination.Id, StorageOperationKind.Verify, exception.Category);
                    }
                }
            }
        }
        return repaired;
    }
}

public sealed class StorageReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    StorageConfigurationResolver resolver,
    ILogger<StorageReconciliationWorker> logger) : BackgroundService
{
    private readonly EffectiveStorageConfiguration configuration = resolver.Resolve();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.Mode != StorageMode.AsyncMirror)
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<StorageReconciliationRunner>()
                    .ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Storage reconciliation cycle failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
