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
    private int reconciliationPaused;

    public StorageProtectionMetrics()
    {
        meter.CreateObservableGauge("garagebalance.storage.objects.protected", () => protectedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.degraded", () => degradedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.failed", () => failedCount);
        meter.CreateObservableGauge("garagebalance.storage.objects.pending", () => pendingCount);
        meter.CreateObservableGauge("garagebalance.storage.reconciliation.paused", () => reconciliationPaused);
    }

    public void Update(StorageProtectionSummary summary, bool paused = false)
    {
        Volatile.Write(ref protectedCount, summary.Protected);
        Volatile.Write(ref degradedCount, summary.Degraded);
        Volatile.Write(ref failedCount, summary.Failed);
        Volatile.Write(ref pendingCount, summary.Pending);
        Volatile.Write(ref reconciliationPaused, paused ? 1 : 0);
    }

    public void Dispose() => meter.Dispose();
}

public sealed record StorageProtectionSummary(int Protected, int Degraded, int Failed, int Pending, int Deleting)
{
    public static async Task<StorageProtectionSummary> ReadAsync(IStorageCatalog catalog, string tenantId,
        IReadOnlyCollection<StorageDataClass> dataClasses, CancellationToken cancellationToken)
    {
        var summary = new StorageProtectionSummary(0, 0, 0, 0, 0);
        foreach (var dataClass in dataClasses.Distinct())
        {
            string? cursor = null;
            do
            {
                var page = await catalog.ExportManifestPageAsync(tenantId, dataClass, 100, cursor, cancellationToken);
                summary = summary with
                {
                    Protected = summary.Protected + page.Items.Count(item => item.State == StorageObjectState.Protected),
                    Degraded = summary.Degraded + page.Items.Count(item => item.State == StorageObjectState.ProtectionDegraded),
                    Failed = summary.Failed + page.Items.Count(item => item.State == StorageObjectState.Failed),
                    Pending = summary.Pending + page.Items.Count(item => item.State is StorageObjectState.CreatedLocal or StorageObjectState.ProtectionPending),
                    Deleting = summary.Deleting + page.Items.Count(item => item.State == StorageObjectState.Deleting)
                };
                cursor = page.NextCursor;
            } while (cursor is not null);
        }
        return summary;
    }
}

public sealed class StorageProtectionMonitor(
    IServiceScopeFactory scopeFactory,
    StorageConfigurationResolver resolver,
    StorageProtectionMetrics metrics,
    ILogger<StorageProtectionMonitor> logger,
    IStorageReconciliationGuard? safetyGuard = null) : BackgroundService
{
    private readonly EffectiveStorageConfiguration configuration = resolver.Resolve();
    private StorageProtectionSummary? previous;
    private bool? previouslyPaused;

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
                var current = await StorageProtectionSummary.ReadAsync(scope.ServiceProvider.GetRequiredService<IStorageCatalog>(),
                    configuration.TenantId, configuration.Policies.Select(item => item.DataClass).Distinct().ToArray(), stoppingToken);
                var safety = safetyGuard is null ? null : await safetyGuard.GetStateAsync(stoppingToken);
                var paused = safety?.Paused == true;
                metrics.Update(current, paused);
                if (paused != previouslyPaused)
                {
                    if (paused)
                    {
                        logger.LogCritical("Automatic storage repair is paused. Category={Category}; Anomalies={Anomalies}", safety?.Category, safety?.AnomalyCount);
                    }
                    else if (previouslyPaused == true)
                    {
                        logger.LogInformation("Automatic storage repair was resumed by the operator.");
                    }
                    previouslyPaused = paused;
                }
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
