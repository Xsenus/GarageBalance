using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AnnualAccrualPolicyTests
{
    [Theory]
    [InlineData(null, null, 2026)]
    [InlineData(null, true, 2026)]
    [InlineData(null, false, null)]
    [InlineData(2025, null, 2025)]
    [InlineData(2025, true, 2025)]
    [InlineData(2025, false, 2025)]
    public void HistoricalObligationYearPreservesExplicitYearAndExcludesKnownNonannualHistory(
        int? year, bool? annual, int? expected)
    {
        Assert.Equal(expected, AnnualAccrualPolicy.ResolveHistoricalObligationYear(year, annual, new(2026, 10, 1)));
    }

    [Theory]
    [InlineData("membership")]
    [InlineData(" TARGET ")]
    [InlineData("Outdoor_Lighting")]
    public void ResolveAccountingYear_ReturnsYearForAnnualIncomeTypes(string incomeTypeCode)
    {
        var result = AnnualAccrualPolicy.ResolveAccountingYear(incomeTypeCode, new DateOnly(2027, 6, 1));

        Assert.Equal(2027, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("electricity")]
    public void ResolveAccountingYear_ReturnsNullForOtherIncomeTypes(string? incomeTypeCode)
    {
        var result = AnnualAccrualPolicy.ResolveAccountingYear(incomeTypeCode, new DateOnly(2027, 6, 1));

        Assert.Null(result);
    }

    [Fact]
    public void ResolveAccountingYear_UsesConfiguredPeriodicityBeforeLegacyIncomeTypeCode()
    {
        var month = new DateOnly(2027, 1, 1);

        Assert.Equal(2027, AnnualAccrualPolicy.ResolveAccountingYear("custom_service", month, periodicityMonths: 12));
        Assert.Null(AnnualAccrualPolicy.ResolveAccountingYear("membership", month, periodicityMonths: 1));
    }

    [Theory]
    [InlineData(1000, 999, false)]
    [InlineData(1000, 1000, true)]
    [InlineData(1000, 1200, true)]
    [InlineData(0, 0, false)]
    public void IsFullyPaid_RequiresTheWholePositiveAccrual(decimal accrued, decimal allocated, bool expected)
    {
        Assert.Equal(expected, AnnualAccrualPolicy.IsFullyPaid(accrued, allocated));
    }
}
