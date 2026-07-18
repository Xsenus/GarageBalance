using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AnnualAccrualPolicyTests
{
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
