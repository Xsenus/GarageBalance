using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Tests.Common;

public sealed class MonthPeriodTests
{
    [Fact]
    public void Normalize_ReturnsFirstDayOfMonth()
    {
        var result = MonthPeriod.Normalize(new DateOnly(2026, 6, 23));

        Assert.Equal(new DateOnly(2026, 6, 1), result);
    }

    [Fact]
    public void Enumerate_ReturnsNormalizedMonthStarts()
    {
        var result = MonthPeriod.Enumerate(new DateOnly(2026, 5, 31), new DateOnly(2026, 7, 15)).ToList();

        Assert.Equal(
            [
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 7, 1)
            ],
            result);
    }
}
