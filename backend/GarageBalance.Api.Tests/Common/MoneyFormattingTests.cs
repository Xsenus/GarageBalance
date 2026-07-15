using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Tests.Common;

public sealed class MoneyFormattingTests
{
    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("7.5", "7.50")]
    [InlineData("1000000", "1 000 000.00")]
    [InlineData("-42131.25", "-42 131.25")]
    public void Format_UsesUnifiedAccountingPresentation(string value, string expected)
    {
        Assert.Equal(expected, MoneyFormatting.Format(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
