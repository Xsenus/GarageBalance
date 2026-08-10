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

    [Theory]
    [InlineData(TariffCalculationBases.Fixed, "руб.", true)]
    [InlineData(TariffCalculationBases.Fixed, "руб./гараж", true)]
    [InlineData(TariffCalculationBases.People, "человек", true)]
    [InlineData(TariffCalculationBases.MeterWater, "куб. м", true)]
    [InlineData(TariffCalculationBases.MeterElectricity, "кВт·ч", true)]
    [InlineData(TariffCalculationBases.MeterElectricity, "кВт", false)]
    [InlineData(TariffCalculationBases.MeterWater, "руб.", false)]
    [InlineData(TariffCalculationBases.Fixed, " ", false)]
    public void IsCompatibleUnitName_RecognizesOnlyEquivalentUnits(string calculationBase, string unitName, bool expected)
    {
        Assert.Equal(expected, TariffCalculationBases.IsCompatibleUnitName(calculationBase, unitName));
    }

    [Fact]
    public void GetUnitNames_ReturnsCanonicalUnitFirst()
    {
        Assert.Equal(["м³", "куб. м"], TariffCalculationBases.GetUnitNames(TariffCalculationBases.MeterWater));
    }

    [Theory]
    [InlineData(TariffCalculationBases.Fixed, " РУБ./ГАРАЖ ", "руб./гараж")]
    [InlineData(TariffCalculationBases.MeterWater, "руб.", "м³")]
    [InlineData(TariffCalculationBases.MeterWater, " гал. ", "гал.")]
    [InlineData(TariffCalculationBases.MeterElectricity, null, "кВт·ч")]
    public void NormalizeUnitName_CanonicalizesKnownUnitsAndPreservesCustomValues(
        string calculationBase,
        string? unitName,
        string expected)
    {
        Assert.Equal(expected, TariffCalculationBases.NormalizeUnitName(calculationBase, unitName));
    }
}
