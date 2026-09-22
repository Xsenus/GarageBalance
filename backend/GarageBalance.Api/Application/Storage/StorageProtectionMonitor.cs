using System.Diagnostics.Metrics;
using GarageBalance.Api.Domain.Storage;

namespace GarageBalance.Api.Application.Storage;

public sealed class StorageProtectionMetrics : IDisposable
{
    private readonly Meter meter = new("GarageBalance.Storage.Protection", "1.0.0");
    private int protectedCount;
    private int degradedCount;
    private int failedCount;
    private int pendingCount;

    public StorageProtectionMetrics()
    {
        meter.CreateObservableGauge("garagebalance.storage.objects.protected", () => protectedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.degraded", () => degradedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.failed", () => failedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.pending", () => pendingCount);
    }

    public void Update(StorageProtectionSummary summary)
    {
        Volatile.Write(ref protectedCount, summary.Protected);
        Volatile.Write(ref degradedCount, summary.Degraded);
        Volatile.Write(ref failedCount, summary.Failed);
        Volatile.Write(ref pendingCount, summary.Pending);
    }

    public void Dispose() => meter.Dispose();
}

public sealed record StorageProtectionSummary(int Protected, int Degraded, int Failed, int Pending, int Deleting);

public sealed class StorageProtectionMonitor(
    IServiceScopeFactory scopeFactory,
    StorageConfigurationResolver resolver,
    StorageProtectionMetrics metrics,
    ILogger<StorageProtectionMonitor> logger) : BackgroundService
{
    private readonly EffectiveStorageConfiguration configuration = resolver.Resolve();
    private StorageProtectionSummary? previous;

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
                var manifest = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ExportManifestAsync(StorageDataClass.DatabaseBackup, 500, stoppingToken);
                var current = new StorageProtectionSummary(
                    manifest.Count(item => item.State == StorageObjectState.Protected),
                    manifest.Count(item => item.State == StorageObjectState.ProtectionDegraded),
                    manifest.Count(item => item.State == StorageObjectState.Failed),
                    manifest.Count(item => item.State is StorageObjectState.CreatedLocal or StorageObjectState.ProtectionPending),
                    manifest.Count(item => item.State == StorageObjectState.Deleting));
                metrics.Update(current);
                if (current != previous)
                {
                    if (current.Failed > 0)
                    {
                        logger.LogCritical("Storage protection has failed objects. Failed={Failed}; Degraded={Degraded}; Pending={Pending}", current.Failed, current.Degraded, current.Pending);
                    }
                    else if (current.Degraded > 0 || current.Pending > 0)
                    {
                        logger.LogWarning("Storage protection is incomplete. Degraded={Degraded}; Pending={Pending}; Protected={Protected}", current.Degraded, current.Pending, current.Protected);
                    }
                    else if (previous is { Failed: > 0 } or { Degraded: > 0 } or { Pending: > 0 })
                    {
                        logger.LogInformation("Storage protection recovered. Protected={Protected}", current.Protected);
                    }
                    previous = current;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Storage protection monitoring failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
