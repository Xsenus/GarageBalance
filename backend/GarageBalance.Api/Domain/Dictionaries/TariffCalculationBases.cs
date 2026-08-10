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

    private static readonly IReadOnlyDictionary<string, string[]> CompatibleUnitNames =
        new Dictionary<string, string[]>
        {
            [Fixed] = ["руб.", "руб./гараж"],
            [People] = ["чел.", "человек"],
            [MeterWater] = ["м³", "куб. м"],
            [MeterElectricity] = ["кВт·ч"]
        };

    public static bool IsSupported(string calculationBase)
    {
        return SupportedValues.Contains(calculationBase);
    }

    public static string GetUnitName(string calculationBase)
    {
        return GetUnitNames(calculationBase)[0];
    }

    public static IReadOnlyList<string> GetUnitNames(string calculationBase)
    {
        if (!CompatibleUnitNames.TryGetValue(calculationBase, out var unitNames))
        {
            throw new ArgumentOutOfRangeException(nameof(calculationBase), calculationBase, "Unsupported tariff calculation base.");
        }

        return unitNames;
    }

    public static bool IsCompatibleUnitName(string calculationBase, string? unitName)
    {
        if (string.IsNullOrWhiteSpace(unitName))
        {
            return false;
        }

        return GetUnitNames(calculationBase)
            .Any(candidate => string.Equals(candidate, unitName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeUnitName(string calculationBase, string? unitName)
    {
        var compatibleUnitNames = GetUnitNames(calculationBase);
        var trimmedUnitName = unitName?.Trim() ?? string.Empty;
        var compatibleUnitName = compatibleUnitNames.FirstOrDefault(
            candidate => string.Equals(candidate, trimmedUnitName, StringComparison.OrdinalIgnoreCase));
        if (compatibleUnitName is not null)
        {
            return compatibleUnitName;
        }

        var isKnownForAnotherCalculationBase = CompatibleUnitNames.Values
            .SelectMany(unitNames => unitNames)
            .Any(candidate => string.Equals(candidate, trimmedUnitName, StringComparison.OrdinalIgnoreCase));

        return !string.IsNullOrWhiteSpace(trimmedUnitName) && !isKnownForAnotherCalculationBase
            ? trimmedUnitName
            : compatibleUnitNames[0];
    }
}
