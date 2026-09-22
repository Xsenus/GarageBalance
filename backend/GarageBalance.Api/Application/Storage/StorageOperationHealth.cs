using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace GarageBalance.Api.Application.Storage;

public enum StorageOperationKind { Read, Write, Delete, Verify }
public enum StorageOperationHealthState { Healthy, Degraded, Unavailable, Recovering }

public sealed record StorageOperationHealthSnapshot(
    string DestinationId,
    StorageOperationKind Operation,
    StorageOperationHealthState State,
    int ConsecutiveFailures,
    DateTimeOffset? RetryAtUtc,
    string? LastCategory,
    DateTimeOffset UpdatedAtUtc);

public sealed class StorageOperationHealthTracker(TimeProvider timeProvider)
{
    private static readonly Meter Meter = new("GarageBalance.Storage", "1.0.0");
    private static readonly Counter<long> OperationSuccesses = Meter.CreateCounter<long>("garagebalance.storage.operation.successes");
    private static readonly Counter<long> OperationFailures = Meter.CreateCounter<long>("garagebalance.storage.operation.failures");
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<(string DestinationId, StorageOperationKind Operation), Entry> entries = new();

    public bool TryBeginAttempt(string destinationId, StorageOperationKind operation)
    {
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            var now = timeProvider.GetUtcNow();
            if (entry.State != StorageOperationHealthState.Unavailable)
            {
                return entry.State != StorageOperationHealthState.Recovering || !entry.ProbeInFlight;
            }
            if (entry.RetryAtUtc > now)
            {
                return false;
            }
            entry.State = StorageOperationHealthState.Recovering;
            entry.ProbeInFlight = true;
            entry.UpdatedAtUtc = now;
            return true;
        }
    }

    public void RecordSuccess(string destinationId, StorageOperationKind operation)
    {
        OperationSuccesses.Add(1, new KeyValuePair<string, object?>("destination", destinationId), new KeyValuePair<string, object?>("operation", operation.ToString()));
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            entry.State = StorageOperationHealthState.Healthy;
            entry.ConsecutiveFailures = 0;
            entry.RetryAtUtc = null;
            entry.LastCategory = null;
            entry.ProbeInFlight = false;
            entry.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
    }

    public void RecordFailure(string destinationId, StorageOperationKind operation, StorageErrorCategory category)
    {
        if (category is StorageErrorCategory.ObjectMissing or StorageErrorCategory.Conflict or
            StorageErrorCategory.ChecksumOrStale or StorageErrorCategory.ArchivePending or
            StorageErrorCategory.Cancelled)
        {
            return;
        }
        OperationFailures.Add(1,
            new KeyValuePair<string, object?>("destination", destinationId),
            new KeyValuePair<string, object?>("operation", operation.ToString()),
            new KeyValuePair<string, object?>("category", category.ToString()));
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            entry.ConsecutiveFailures++;
            entry.LastCategory = category.ToString();
            entry.ProbeInFlight = false;
            entry.UpdatedAtUtc = timeProvider.GetUtcNow();
            var immediateOpen = category is StorageErrorCategory.CredentialsExpired or
                StorageErrorCategory.ProviderForbidden or StorageErrorCategory.QuotaOrReadOnly or
                StorageErrorCategory.TlsSecurity or StorageErrorCategory.ValidationOrUnsupported;
            if (immediateOpen || entry.ConsecutiveFailures >= 3)
            {
                entry.State = StorageOperationHealthState.Unavailable;
                entry.RetryAtUtc = entry.UpdatedAtUtc.Add(Cooldown);
            }
            else
            {
                entry.State = StorageOperationHealthState.Degraded;
            }
        }
    }

    public IReadOnlyList<StorageOperationHealthSnapshot> Snapshot() => entries
        .Select(item =>
        {
            lock (item.Value)
            {
                return new StorageOperationHealthSnapshot(
                    item.Key.DestinationId,
                    item.Key.Operation,
                    item.Value.State,
                    item.Value.ConsecutiveFailures,
                    item.Value.RetryAtUtc,
                    item.Value.LastCategory,
                    item.Value.UpdatedAtUtc);
            }
        })
        .OrderBy(item => item.DestinationId, StringComparer.Ordinal)
        .ThenBy(item => item.Operation)
        .ToArray();

    private sealed class Entry(DateTimeOffset now)
    {
        public StorageOperationHealthState State { get; set; } = StorageOperationHealthState.Healthy;
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? RetryAtUtc { get; set; }
        public string? LastCategory { get; set; }
        public bool ProbeInFlight { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = now;
    }
}
