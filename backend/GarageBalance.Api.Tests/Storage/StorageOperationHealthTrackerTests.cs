using GarageBalance.Api.Application.Storage;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageOperationHealthTrackerTests
{
    [Fact]
    public void ReadAndWriteCircuitsAreIndependentAndHalfOpenIsLimited()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero));
        var tracker = new StorageOperationHealthTracker(clock);

        tracker.RecordFailure("offsite-a", StorageOperationKind.Write, StorageErrorCategory.ProviderForbidden);

        Assert.False(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Write));
        Assert.True(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Read));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Write));
        Assert.False(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Write));
        tracker.RecordSuccess("offsite-a", StorageOperationKind.Write);
        Assert.True(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Write));
    }

    [Fact]
    public void ObjectMissingDoesNotDisableDestinationButRepeatedNetworkFailureDoes()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);

        tracker.RecordFailure("offsite-a", StorageOperationKind.Read, StorageErrorCategory.ObjectMissing);
        Assert.True(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Read));
        tracker.RecordFailure("offsite-a", StorageOperationKind.Read, StorageErrorCategory.TransientNetwork);
        tracker.RecordFailure("offsite-a", StorageOperationKind.Read, StorageErrorCategory.TransientNetwork);
        Assert.True(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Read));
        tracker.RecordFailure("offsite-a", StorageOperationKind.Read, StorageErrorCategory.TransientNetwork);
        Assert.False(tracker.TryBeginAttempt("offsite-a", StorageOperationKind.Read));
        var snapshot = Assert.Single(tracker.Snapshot());
        Assert.Equal(StorageOperationHealthState.Unavailable, snapshot.State);
        Assert.Equal(3, snapshot.ConsecutiveFailures);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
