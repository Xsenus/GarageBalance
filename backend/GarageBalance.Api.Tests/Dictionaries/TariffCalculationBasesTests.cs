using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class TariffCalculationBasesTests
{
    [Theory]
    [InlineData(TariffCalculationBases.Fixed, "руб.")]
    [InlineData(TariffCalculationBases.People, "чел.")]
    [InlineData(TariffCalculationBases.MeterWater, "м³")]
    [InlineData(TariffCalculationBases.MeterElectricity, "кВт·ч")]
    public void GetUnitName_ReturnsUnitForSupportedCalculationBase(string calculationBase, string expectedUnitName)
    {
        Assert.Equal(expectedUnitName, TariffCalculationBases.GetUnitName(calculationBase));
    }

    [Fact]
    public void GetUnitName_RejectsUnsupportedCalculationBase()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TariffCalculationBases.GetUnitName("unknown"));

        Assert.Equal("calculationBase", exception.ParamName);
    }
}
