using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class AccountingTypeCodePolicyTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" Water_SUPPLY ", "water_supply")]
    public void Normalize_ReturnsCanonicalOptionalCode(string? source, string? expected)
    {
        Assert.Equal(expected, AccountingTypeCodePolicy.Normalize(source));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("custom_fee_2026", true)]
    [InlineData("2_custom", false)]
    [InlineData("custom-fee", false)]
    [InlineData("водоснабжение", false)]
    public void IsValid_AcceptsOnlyStableAsciiIdentifiers(string? code, bool expected)
    {
        Assert.Equal(expected, AccountingTypeCodePolicy.IsValid(code));
    }

    [Theory]
    [InlineData("water", true)]
    [InlineData("debt_transfer", true)]
    [InlineData("custom_income", false)]
    public void IsReservedIncomeCode_RecognizesSystemSemantics(string code, bool expected)
    {
        Assert.Equal(expected, AccountingTypeCodePolicy.IsReservedIncomeCode(code));
    }

    [Theory]
    [InlineData("salary", true)]
    [InlineData("water_supply", true)]
    [InlineData("custom_expense", false)]
    public void IsReservedExpenseCode_RecognizesSystemSemantics(string code, bool expected)
    {
        Assert.Equal(expected, AccountingTypeCodePolicy.IsReservedExpenseCode(code));
    }
}
