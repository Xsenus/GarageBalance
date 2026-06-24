namespace GarageBalance.Api.Domain.Dictionaries;

public static class TariffCalculationBases
{
    public const string Fixed = "fixed";
    public const string People = "people";
    public const string MeterWater = "meter_water";
    public const string MeterElectricity = "meter_electricity";

    private static readonly HashSet<string> SupportedValues =
    [
        Fixed,
        People,
        MeterWater,
        MeterElectricity
    ];

    public static bool IsSupported(string calculationBase)
    {
        return SupportedValues.Contains(calculationBase);
    }
}
