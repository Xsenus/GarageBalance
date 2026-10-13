using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageAccountingPeriodTests
{
    [Theory]
    [InlineData("2026-09-01", "2026-10-13")]
    [InlineData("2026-12-01", "2026-10-13")]
    [InlineData("2025-12-01", "2026-01-13")]
    [InlineData("1900-01-01", "2026-10-13")]
    [InlineData("9999-12-01", "2026-10-13")]
    public void ExplicitMonthIsIndependentOfTheRegistrationDate(string start, string registration)
    {
        var month = DateOnly.Parse(start);
        Assert.True(GarageAccountingPeriod.IsValidStartMonth(month));
        Assert.Equal(month, GarageAccountingPeriod.ResolveStartMonth(month, DateOnly.Parse(registration)));
    }

    [Theory]
    [InlineData("2026-09-16", "2026-09-01")]
    [InlineData("2026-12-31", "2026-12-01")]
    [InlineData("2027-01-01", "2027-01-01")]
    [InlineData("1899-12-31", "1899-12-01")]
    public void MissingExplicitMonthPreservesLegacyRegistrationMonth(string registration, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), GarageAccountingPeriod.ResolveStartMonth(null, DateOnly.Parse(registration)));
    }

    [Theory]
    [InlineData("2026-09-02")]
    [InlineData("2026-12-31")]
    [InlineData("1899-12-01")]
    public void RejectsInvalidExplicitMonthWithoutChangingTheFallback(string start)
    {
        var month = DateOnly.Parse(start);
        Assert.False(GarageAccountingPeriod.IsValidStartMonth(month));
        Assert.Throws<ArgumentOutOfRangeException>(() => GarageAccountingPeriod.ResolveStartMonth(month, new DateOnly(2026, 10, 13)));
        Assert.Equal(new DateOnly(2026, 10, 1), GarageAccountingPeriod.ResolveStartMonth(null, new DateOnly(2026, 10, 13)));
    }
}
