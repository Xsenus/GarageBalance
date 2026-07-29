using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Tests.Common;

public sealed class QueryLimitsTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(25, 25)]
    [InlineData(501, 500)]
    public void NormalizeListSize_AppliesSharedDefaultsAndMaximum(int? requestedSize, int expected)
    {
        Assert.Equal(expected, QueryLimits.NormalizeListSize(requestedSize));
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(100, 100)]
    [InlineData(501, 500)]
    public void NormalizePageSize_AppliesSharedDefaultsAndMaximum(int requestedSize, int expected)
    {
        Assert.Equal(expected, QueryLimits.NormalizePageSize(requestedSize));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    [InlineData(101, 100)]
    public void NormalizeListSize_RejectsInvalidFeatureBounds(int defaultSize, int maximumSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QueryLimits.NormalizeListSize(10, defaultSize, maximumSize));
    }

    [Theory]
    [InlineData(2017, 1, 2026, 12, false)]
    [InlineData(2016, 12, 2026, 12, true)]
    [InlineData(2026, 7, 2026, 6, false)]
    public void ExceedsMaximumReportPeriod_UsesInclusiveCalendarMonths(
        int fromYear,
        int fromMonth,
        int toYear,
        int toMonth,
        bool expected)
    {
        var periodFrom = new DateOnly(fromYear, fromMonth, 1);
        var periodTo = new DateOnly(toYear, toMonth, DateTime.DaysInMonth(toYear, toMonth));

        Assert.Equal(expected, QueryLimits.ExceedsMaximumReportPeriod(periodFrom, periodTo));
    }
}
