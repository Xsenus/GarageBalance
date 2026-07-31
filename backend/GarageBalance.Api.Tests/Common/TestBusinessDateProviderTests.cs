using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Tests.Common;

public sealed class TestBusinessDateProviderTests
{
    [Fact]
    public void FromWithoutTimeProvider_UsesSameLocalCalendarAsCurrentMonth()
    {
        var provider = TestBusinessDateProvider.From(null);

        Assert.Equal(MonthPeriod.CurrentLocalMonth(), MonthPeriod.Normalize(provider.Today));
    }

    [Fact]
    public void FromFixedTimeProvider_RemainsDeterministic()
    {
        var provider = TestBusinessDateProvider.From(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 20, 30, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 7, 31), provider.Today);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
