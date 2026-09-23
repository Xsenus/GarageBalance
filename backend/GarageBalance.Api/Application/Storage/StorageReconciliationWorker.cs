using GarageBalance.Api.Domain.Storage;
using System.Runtime.CompilerServices;

namespace GarageBalance.Api.Application.Storage;

public sealed class StorageReconciliationRunner(
    IStorageCatalog catalog,
    IStorageProviderRegistry providers,
    StorageConfigurationResolver resolver,
    StorageOperationHealthTracker health,
    TimeProvider timeProvider,
    ILogger<StorageReconciliationRunner> logger,
    IStorageReconciliationGuard? safetyGuard = null)
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
        if (safetyGuard is not null)
        {
            var safety = await safetyGuard.GetStateAsync(cancellationToken);
            if (safety.Paused || !safety.PersistenceAvailable)
            {
                logger.LogWarning("Storage reconciliation is paused by its durable safety state. Category={Category}", safety.Category);
                if (logicalKey is not null)
                {
                    throw new StorageProviderException(StorageErrorCategory.ValidationOrUnsupported,
                        "Storage reconciliation is paused; an operator must explicitly resume it.");
                }
                return 0;
            }
        }
        var repaired = 0;
        var anomalies = 0;
        var repairPlans = new List<(StorageManifestEntry Object, string DestinationId, StorageReplicaState State, StoragePolicyOptions Policy)>();
        foreach (var dataClass in configuration.Policies.Select(item => item.DataClass).Distinct())
        {
            var policy = configuration.Policies.Single(item => item.DataClass == dataClass);
            await foreach (var storageObject in EnumerateObjectsAsync(dataClass, logicalKey, cancellationToken))
            {
                if (storageObject.State is StorageObjectState.Deleting or StorageObjectState.Deleted)
                {
                    continue;
                }
                foreach (var replica in storageObject.Replicas.Where(item => item.State == StorageReplicaState.Available))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destination = configuration.Destinations.SingleOrDefault(item =>
                        string.Equals(item.Id, replica.DestinationId, StringComparison.Ordinal));
                    if (destination is null || destination.State == StorageDestinationState.Disabled ||
                        !destination.Capabilities.HasFlag(StorageCapability.Stat) ||
                        !health.TryBeginAttempt(destination.Id, StorageOperationKind.Verify, out var attemptEpoch))
                    {
                        continue;
                    }
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(configuration.Replication.OperationDeadlineSeconds));
                        var stat = await providers.GetRequired(destination.Id).StatAsync(replica.NativeLocator, deadline.Token);
                        health.RecordSuccess(destination.Id, StorageOperationKind.Verify, attemptEpoch);
                        var observedState = stat is null
                            ? StorageReplicaState.Missing
                            : stat.SizeBytes != storageObject.SizeBytes ||
                              !string.Equals(stat.ProviderChecksum, storageObject.Sha256, StringComparison.OrdinalIgnoreCase)
                                ? StorageReplicaState.Corrupted
                                : StorageReplicaState.Available;
                        if (observedState == StorageReplicaState.Available)
                        {
                            await catalog.RecordReplicaVerifiedAsync(storageObject.ObjectId, destination.Id,
                                storageObject.Generation, storageObject.Sha256, storageObject.SizeBytes,
                                timeProvider.GetUtcNow(), cancellationToken);
                            continue;
                        }
                        anomalies++;
                        if (anomalies > MaximumAnomaliesPerRun)
                        {
                            if (safetyGuard is not null)
                            {
                                await safetyGuard.PauseAsync(anomalies, cancellationToken);
                            }
                            logger.LogCritical("Storage reconciliation stopped after the anomaly safety threshold was reached.");
                            return 0;
                        }
                        repairPlans.Add((storageObject, destination.Id, observedState, policy));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        health.RecordFailure(destination.Id, StorageOperationKind.Verify, StorageErrorCategory.TransientNetwork, attemptEpoch);
                    }
                    catch (StorageProviderException exception)
                    {
                        health.RecordFailure(destination.Id, StorageOperationKind.Verify, exception.Category, attemptEpoch);
                    }
                    finally
                    {
                        health.AbandonAttempt(destination.Id, StorageOperationKind.Verify, attemptEpoch);
                    }
                }
            }
        }
        foreach (var plan in repairPlans)
        {
            if (safetyGuard is not null && (await safetyGuard.GetStateAsync(cancellationToken)).Paused)
            {
                break;
            }
            if (await catalog.ScheduleRepairAsync(plan.Object.ObjectId, plan.DestinationId, plan.State,
                    plan.State.ToString(), "Storage reconciliation detected a missing or corrupt replica.",
                    plan.Policy.RequiredIndependentCopies, plan.Policy.DesiredCopies,
                    configuration.Replication.MaximumAttempts, timeProvider.GetUtcNow(), cancellationToken))
            {
                repaired++;
            }
        }
        return repaired;
    }

    private async IAsyncEnumerable<StorageManifestEntry> EnumerateObjectsAsync(
        StorageDataClass dataClass,
        string? logicalKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (logicalKey is not null)
        {
            var item = await catalog.FindByLogicalKeyAsync(configuration.TenantId, dataClass, logicalKey, cancellationToken);
            if (item is not null)
            {
                yield return new StorageManifestEntry(item.Id, item.OperationId, item.DataClass,
                    item.LogicalKey, item.CommittedGeneration, item.SizeBytes, item.Sha256, item.State,
                    item.CreatedAtUtc, item.UpdatedAtUtc,
                    item.Replicas.Select(replica => new StorageManifestReplicaEntry(replica.DestinationId,
                        replica.FailureDomain, replica.NativeLocator, replica.Generation, replica.State,
                        replica.SizeBytes, replica.Sha256, replica.LastVerifiedAtUtc)).ToArray());
            }
            yield break;
        }
        string? cursor = null;
        do
        {
            var page = await catalog.ExportManifestPageAsync(configuration.TenantId, dataClass, 100, cursor, cancellationToken);
            foreach (var item in page.Items)
            {
                yield return item;
            }
            cursor = page.NextCursor;
        } while (cursor is not null);
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
