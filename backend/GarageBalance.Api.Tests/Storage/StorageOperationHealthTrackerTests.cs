using GarageBalance.Api.Application.Storage;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageOperationHealthTrackerTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("abandon")]
    public void StaleAttemptCannotCompleteOrReleaseANewerHalfOpenProbe(string completion)
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write, out var oldEpoch));
        tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.ProviderForbidden, oldEpoch);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write, out var probeEpoch));

        if (completion == "success")
        {
            tracker.RecordSuccess("a", StorageOperationKind.Write, oldEpoch);
        }
        else if (completion == "failure")
        {
            tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.TransientNetwork, oldEpoch);
        }
        else
        {
            tracker.AbandonAttempt("a", StorageOperationKind.Write, oldEpoch);
        }

        Assert.Equal(StorageOperationHealthState.Recovering, Assert.Single(tracker.Snapshot()).State);
        Assert.False(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
        tracker.RecordSuccess("a", StorageOperationKind.Write, probeEpoch);
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
    }

    [Fact]
    public void FailedHalfOpenProbeReopensEvenBelowOrdinaryFailureThreshold()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);
        tracker.RecordFailure("a", StorageOperationKind.Read, StorageErrorCategory.ProviderForbidden);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Read, out var epoch));
        tracker.RecordFailure("a", StorageOperationKind.Read, StorageErrorCategory.TransientNetwork, epoch);
        Assert.Equal(StorageOperationHealthState.Unavailable, Assert.Single(tracker.Snapshot()).State);
        Assert.False(tracker.TryBeginAttempt("a", StorageOperationKind.Read));
    }

    [Fact]
    public void RejectedAttemptDoesNotExtendCooldownOrReleaseActiveProbe()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);
        tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.ProviderForbidden);
        var retryAt = Assert.Single(tracker.Snapshot()).RetryAtUtc;
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(tracker.TryBeginAttempt("a", StorageOperationKind.Write, out var rejectedEpoch));
        tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.TransientNetwork, rejectedEpoch);
        tracker.AbandonAttempt("a", StorageOperationKind.Write, rejectedEpoch);
        Assert.Equal(retryAt, Assert.Single(tracker.Snapshot()).RetryAtUtc);
        clock.Advance(TimeSpan.FromSeconds(21));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
        tracker.AbandonAttempt("a", StorageOperationKind.Write, rejectedEpoch);
        Assert.False(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
    }

    [Theory]
    [InlineData(StorageErrorCategory.ObjectMissing)]
    [InlineData(StorageErrorCategory.ChecksumOrStale)]
    [InlineData(StorageErrorCategory.Conflict)]
    [InlineData(StorageErrorCategory.ArchivePending)]
    public void ObjectLevelHalfOpenResponseReleasesProbe(StorageErrorCategory category)
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);
        tracker.RecordFailure("a", StorageOperationKind.Read, StorageErrorCategory.ProviderForbidden);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Read));
        tracker.RecordFailure("a", StorageOperationKind.Read, category);
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Read));
        Assert.Equal(StorageOperationHealthState.Healthy, Assert.Single(tracker.Snapshot()).State);
    }

    [Fact]
    public void CancelledHalfOpenProbeCanBeRetriedAfterCooldown()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        var tracker = new StorageOperationHealthTracker(clock);
        tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.ProviderForbidden);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
        tracker.RecordFailure("a", StorageOperationKind.Write, StorageErrorCategory.Cancelled);
        Assert.False(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
        tracker.AbandonAttempt("a", StorageOperationKind.Write);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(tracker.TryBeginAttempt("a", StorageOperationKind.Write));
    }

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
