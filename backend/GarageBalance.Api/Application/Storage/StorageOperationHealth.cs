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

    public bool TryBeginAttempt(string destinationId, StorageOperationKind operation) =>
        TryBeginAttempt(destinationId, operation, out _);

    public bool TryBeginAttempt(string destinationId, StorageOperationKind operation, out long attemptEpoch)
    {
        attemptEpoch = -1;
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            var now = timeProvider.GetUtcNow();
            if (entry.State != StorageOperationHealthState.Unavailable)
            {
                if (entry.State == StorageOperationHealthState.Recovering)
                {
                    if (entry.ProbeInFlight)
                    {
                        return false;
                    }
                    entry.ProbeInFlight = true;
                }
                attemptEpoch = entry.Epoch;
                return true;
            }
            if (entry.RetryAtUtc > now)
            {
                return false;
            }
            entry.State = StorageOperationHealthState.Recovering;
            entry.Epoch++;
            attemptEpoch = entry.Epoch;
            entry.ProbeInFlight = true;
            entry.UpdatedAtUtc = now;
            return true;
        }
    }

    public void RecordSuccess(string destinationId, StorageOperationKind operation, long? attemptEpoch = null)
    {
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            if (attemptEpoch is not null && attemptEpoch != entry.Epoch)
            {
                return;
            }
            OperationSuccesses.Add(1, new KeyValuePair<string, object?>("destination", destinationId), new KeyValuePair<string, object?>("operation", operation.ToString()));
            if (entry.State is StorageOperationHealthState.Recovering or StorageOperationHealthState.Unavailable)
            {
                entry.Epoch++;
            }
            entry.State = StorageOperationHealthState.Healthy;
            entry.ConsecutiveFailures = 0;
            entry.RetryAtUtc = null;
            entry.LastCategory = null;
            entry.ProbeInFlight = false;
            entry.UpdatedAtUtc = timeProvider.GetUtcNow();
        }
    }

    public void RecordFailure(string destinationId, StorageOperationKind operation, StorageErrorCategory category, long? attemptEpoch = null)
    {
        if (category is StorageErrorCategory.ObjectMissing or StorageErrorCategory.Conflict or
            StorageErrorCategory.ChecksumOrStale or StorageErrorCategory.ArchivePending or
            StorageErrorCategory.Cancelled)
        {
            if (category == StorageErrorCategory.Cancelled)
            {
                AbandonAttempt(destinationId, operation, attemptEpoch);
            }
            else
            {
                // An object-level response proves endpoint reachability, not object integrity.
                RecordSuccess(destinationId, operation, attemptEpoch);
            }
            return;
        }
        var entry = entries.GetOrAdd((destinationId, operation), _ => new Entry(timeProvider.GetUtcNow()));
        lock (entry)
        {
            if (attemptEpoch is not null && attemptEpoch != entry.Epoch)
            {
                return;
            }
            OperationFailures.Add(1,
                new KeyValuePair<string, object?>("destination", destinationId),
                new KeyValuePair<string, object?>("operation", operation.ToString()),
                new KeyValuePair<string, object?>("category", category.ToString()));
            entry.ConsecutiveFailures++;
            entry.LastCategory = category.ToString();
            entry.ProbeInFlight = false;
            entry.UpdatedAtUtc = timeProvider.GetUtcNow();
            var immediateOpen = category is StorageErrorCategory.CredentialsExpired or
                StorageErrorCategory.ProviderForbidden or StorageErrorCategory.QuotaOrReadOnly or
                StorageErrorCategory.TlsSecurity or StorageErrorCategory.ValidationOrUnsupported;
            if (immediateOpen || entry.State == StorageOperationHealthState.Recovering || entry.ConsecutiveFailures >= 3)
            {
                entry.State = StorageOperationHealthState.Unavailable;
                entry.Epoch++;
                entry.RetryAtUtc = entry.UpdatedAtUtc.Add(Cooldown);
            }
            else
            {
                entry.State = StorageOperationHealthState.Degraded;
            }
        }
    }

    public void AbandonAttempt(string destinationId, StorageOperationKind operation, long? attemptEpoch = null)
    {
        if (!entries.TryGetValue((destinationId, operation), out var entry))
        {
            return;
        }
        lock (entry)
        {
            if (attemptEpoch is not null && attemptEpoch != entry.Epoch)
            {
                return;
            }
            entry.ProbeInFlight = false;
            if (entry.State == StorageOperationHealthState.Recovering)
            {
                entry.State = StorageOperationHealthState.Unavailable;
                entry.Epoch++;
                entry.RetryAtUtc = timeProvider.GetUtcNow().Add(Cooldown);
            }
            entry.UpdatedAtUtc = timeProvider.GetUtcNow();
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
        public long Epoch { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = now;
    }
}
