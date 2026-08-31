using System.Globalization;
using System.Text.Json;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed record RegularAccrualTariffTier(decimal? UpperBound, decimal Rate);

public sealed record RegularAccrualSegmentDefinition(
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string? CalculationBase,
    decimal Rate,
    string UnitName,
    IReadOnlyList<RegularAccrualTariffTier> Tiers);

public sealed record AccrualCalculationTierDto(
    decimal From,
    decimal? To,
    decimal Quantity,
    decimal Rate,
    decimal Amount);

public sealed record AccrualCalculationLineDto(
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    int Days,
    int MonthDays,
    string? CalculationBase,
    string CalculationMode,
    string UnitName,
    decimal Rate,
    decimal Quantity,
    decimal Amount,
    IReadOnlyList<AccrualCalculationTierDto> Tiers,
    string Formula,
    bool HasTariff,
    IReadOnlyList<RegularAccrualTariffTier>? TierDefinitions = null);

public sealed record AccrualCalculationDetailsDto(
    int Version,
    DateOnly AccountingMonth,
    decimal? PreviousMeterValue,
    decimal? CurrentMeterValue,
    decimal? MeterConsumption,
    bool RequiresMeter,
    string? VolumeAllocationRule,
    IReadOnlyList<AccrualCalculationLineDto> Lines,
    decimal TotalAmount,
    decimal? AverageRate = null,
    string? RateAveragingRule = null,
    string? MonthlyCalculationFormula = null);

public sealed record RegularAccrualCalculationResult(
    bool Succeeded,
    decimal Amount,
    AccrualCalculationDetailsDto? Details,
    string? ErrorMessage)
{
    public static RegularAccrualCalculationResult Failure(string message) => new(false, 0m, null, message);

    public static RegularAccrualCalculationResult Success(AccrualCalculationDetailsDto details) =>
        new(true, details.TotalAmount, details, null);
}

public static class RegularAccrualCalculator
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RegularAccrualCalculationResult Calculate(
        Garage garage,
        DateOnly accountingMonth,
        MeterReading? meterReading,
        IReadOnlyList<RegularAccrualSegmentDefinition> definitions)
    {
        var month = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var monthDays = monthEnd.Day;
        var ordered = definitions
            .Select(definition => definition with
            {
                EffectiveFrom = definition.EffectiveFrom < month ? month : definition.EffectiveFrom,
                EffectiveTo = definition.EffectiveTo > monthEnd ? monthEnd : definition.EffectiveTo
            })
            .Where(definition => definition.EffectiveFrom <= definition.EffectiveTo)
            .OrderBy(definition => definition.EffectiveFrom)
            .ToList();
        var activeDefinitions = ordered
            .Where(definition => definition.CalculationBase is not null)
            .ToList();
        var calculationBases = activeDefinitions
            .Select(definition => definition.CalculationBase!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (calculationBases.Length > 1)
        {
            return RegularAccrualCalculationResult.Failure(
                "в течение месяца менялся способ расчёта тарифа. Ставки с разными единицами нельзя усреднить.");
        }

        var requiresMeter = activeDefinitions.Any(IsMetered);
        if (requiresMeter && meterReading is null)
        {
            return RegularAccrualCalculationResult.Failure("нет показания счетчика за месяц.");
        }

        var calculationBase = calculationBases.SingleOrDefault();
        var monthlyQuantity = calculationBase switch
        {
            TariffCalculationBases.Fixed => 1m,
            TariffCalculationBases.People => garage.PeopleCount,
            TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity =>
                MoneyMath.RoundMeterValue(meterReading!.Consumption),
            _ => 0m
        };
        var resolvedRates = activeDefinitions
            .Select(definition => ResolveRate(definition, meterReading?.CurrentValue))
            .ToArray();
        var averageRate = resolvedRates.Length == 0
            ? (decimal?)null
            : resolvedRates.Average(item => item.Rate);
        var total = averageRate.HasValue
            ? MoneyMath.RoundMoney(monthlyQuantity * averageRate.Value)
            : 0m;
        var lines = new List<AccrualCalculationLineDto>(ordered.Count);
        var activeIndex = 0;
        var accumulatedAmount = 0m;
        foreach (var definition in ordered)
        {
            var days = definition.EffectiveTo.DayNumber - definition.EffectiveFrom.DayNumber + 1;
            if (definition.CalculationBase is null)
            {
                lines.Add(new AccrualCalculationLineDto(
                    definition.EffectiveFrom,
                    definition.EffectiveTo,
                    days,
                    monthDays,
                    null,
                    "no_tariff",
                    definition.UnitName,
                    0m,
                    0m,
                    0m,
                    [],
                    "Тариф на этот участок не задан и в среднюю ставку месяца не входит: 0,00",
                    false,
                    definition.Tiers));
                continue;
            }

            var resolved = resolvedRates[activeIndex];
            var isLastActive = activeIndex == resolvedRates.Length - 1;
            var amount = isLastActive
                ? MoneyMath.RoundMoney(total - accumulatedAmount)
                : MoneyMath.RoundMoney(monthlyQuantity * resolved.Rate / resolvedRates.Length);
            accumulatedAmount += amount;
            activeIndex++;
            var allocatedQuantity = monthlyQuantity / resolvedRates.Length;
            var mode = definition.CalculationBase switch
            {
                TariffCalculationBases.Fixed => "fixed",
                TariffCalculationBases.People => "people",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity when definition.Tiers.Count > 0 => "metered_tiered",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity => "metered",
                _ => "no_tariff"
            };
            var tierLines = resolved.IsTiered
                ? new[]
                {
                    new AccrualCalculationTierDto(
                        resolved.LowerBound,
                        resolved.UpperBound,
                        allocatedQuantity,
                        resolved.Rate,
                        amount)
                }
                : [];
            lines.Add(new AccrualCalculationLineDto(
                definition.EffectiveFrom,
                definition.EffectiveTo,
                days,
                monthDays,
                definition.CalculationBase,
                mode,
                definition.UnitName,
                resolved.Rate,
                allocatedQuantity,
                amount,
                tierLines,
                BuildSegmentFormula(mode, monthlyQuantity, resolved.Rate, resolvedRates.Length, amount, meterReading?.CurrentValue),
                true,
                definition.Tiers));
        }

        var details = new AccrualCalculationDetailsDto(
            3,
            month,
            meterReading?.PreviousValue,
            meterReading?.CurrentValue,
            meterReading?.Consumption,
            requiresMeter,
            null,
            lines,
            total,
            averageRate,
            averageRate.HasValue ? BuildRateAveragingRule(resolvedRates.Select(item => item.Rate).ToArray(), averageRate.Value) : null,
            averageRate.HasValue ? BuildMonthlyCalculationFormula(calculationBase!, monthlyQuantity, averageRate.Value, total, activeDefinitions[0].UnitName) : null);
        return RegularAccrualCalculationResult.Success(details);
    }

    public static string Serialize(AccrualCalculationDetailsDto details) => JsonSerializer.Serialize(details, JsonOptions);

    public static AccrualCalculationDetailsDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AccrualCalculationDetailsDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<RegularAccrualSegmentDefinition> FromSnapshot(AccrualCalculationDetailsDto details) =>
        details.Lines.Select(line => new RegularAccrualSegmentDefinition(
            line.EffectiveFrom,
            line.EffectiveTo,
            line.HasTariff ? line.CalculationBase : null,
            line.Rate,
            line.UnitName,
            line.TierDefinitions is { Count: > 0 }
                ? line.TierDefinitions
                : line.Tiers.Select(tier => new RegularAccrualTariffTier(tier.To, tier.Rate)).ToArray()))
            .ToArray();

    private static bool IsMetered(RegularAccrualSegmentDefinition definition) =>
        definition.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;

    private static ResolvedRate ResolveRate(
        RegularAccrualSegmentDefinition definition,
        decimal? currentMeterValue)
    {
        if (!IsMetered(definition) || definition.Tiers.Count == 0)
        {
            return new ResolvedRate(definition.Rate, false, 0m, null);
        }

        var lowerBound = 0m;
        foreach (var tier in definition.Tiers)
        {
            if (!tier.UpperBound.HasValue || currentMeterValue!.Value <= tier.UpperBound.Value)
            {
                return new ResolvedRate(tier.Rate, true, lowerBound, tier.UpperBound);
            }

            lowerBound = tier.UpperBound.Value;
        }

        var fallback = definition.Tiers[^1];
        return new ResolvedRate(fallback.Rate, true, lowerBound, fallback.UpperBound);
    }

    private static string BuildRateAveragingRule(IReadOnlyList<decimal> rates, decimal averageRate) =>
        $"Средняя ставка за месяц: ({string.Join(" + ", rates.Select(rate => rate.ToString("0.####", RussianCulture)))}) / {rates.Count} = {averageRate.ToString("0.####", RussianCulture)}. Количество дней действия ставок на среднее не влияет.";

    private static string BuildMonthlyCalculationFormula(
        string calculationBase,
        decimal monthlyQuantity,
        decimal averageRate,
        decimal amount,
        string unitName)
    {
        var quantity = calculationBase switch
        {
            TariffCalculationBases.Fixed => "1 месяц",
            TariffCalculationBases.People => $"{monthlyQuantity.ToString("0.####", RussianCulture)} чел.",
            _ => $"{monthlyQuantity.ToString("0.###", RussianCulture)} {unitName}"
        };
        return $"Расчёт за месяц: {quantity} × {averageRate.ToString("0.####", RussianCulture)} = {amount.ToString("0.00", RussianCulture)}.";
    }

    private static string BuildSegmentFormula(string mode, decimal monthlyQuantity, decimal rate, int rateCount, decimal amount, decimal? currentMeterValue) => mode switch
    {
        "fixed" => $"Равный вес 1/{rateCount}: 1 месяц × {rate.ToString("0.####", RussianCulture)} / {rateCount} = {amount.ToString("0.00", RussianCulture)}",
        "people" => $"Равный вес 1/{rateCount}: {monthlyQuantity.ToString("0.####", RussianCulture)} чел. × {rate.ToString("0.####", RussianCulture)} / {rateCount} = {amount.ToString("0.00", RussianCulture)}",
        "metered" => $"Равный вес 1/{rateCount}: {monthlyQuantity.ToString("0.###", RussianCulture)} × {rate.ToString("0.####", RussianCulture)} / {rateCount} = {amount.ToString("0.00", RussianCulture)}",
        "metered_tiered" => $"Равный вес 1/{rateCount}: {monthlyQuantity.ToString("0.###", RussianCulture)} × {rate.ToString("0.####", RussianCulture)} / {rateCount} (текущее показание {currentMeterValue?.ToString("0.###", RussianCulture) ?? "—"}) = {amount.ToString("0.00", RussianCulture)}",
        _ => "Тариф на этот участок не задан и в среднюю ставку месяца не входит: 0,00"
    };

    private sealed record ResolvedRate(decimal Rate, bool IsTiered, decimal LowerBound, decimal? UpperBound);
}
