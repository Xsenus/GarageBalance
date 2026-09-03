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
            .Select((definition, index) => new IndexedDefinition(index, definition))
            .Where(item => item.Definition.CalculationBase is not null)
            .ToList();
        if (ordered.Zip(ordered.Skip(1), (left, right) => left.EffectiveTo >= right.EffectiveFrom).Any(overlaps => overlaps))
        {
            return RegularAccrualCalculationResult.Failure("тарифные периоды внутри месяца пересекаются.");
        }
        var calculationBases = activeDefinitions
            .Select(item => item.Definition.CalculationBase!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (calculationBases.Length > 1)
        {
            return RegularAccrualCalculationResult.Failure(
                "в течение месяца менялся способ расчёта тарифа. Ставки с разными единицами нельзя усреднить.");
        }

        var requiresMeter = activeDefinitions.Any(item => IsMetered(item.Definition));
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
        if (monthlyQuantity < 0m)
        {
            return RegularAccrualCalculationResult.Failure("расход по счётчику не может быть отрицательным.");
        }
        var usesTieredTariff = activeDefinitions.Any(item => item.Definition.Tiers.Count > 0);
        var bands = BuildProgressiveBands(monthlyQuantity, activeDefinitions.Select(item => item.Definition));
        var contributions = BuildContributions(activeDefinitions, bands, monthDays);
        var total = contributions.Sum(item => item.Amount);
        var rawTotal = contributions.Sum(item => item.RawAmount);
        var averageRate = activeDefinitions.Count == 0
            ? (decimal?)null
            : monthlyQuantity == 0m
                ? 0m
                : MoneyMath.RoundRate(rawTotal / monthlyQuantity);
        var lines = new List<AccrualCalculationLineDto>(ordered.Count);
        for (var definitionIndex = 0; definitionIndex < ordered.Count; definitionIndex++)
        {
            var definition = ordered[definitionIndex];
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
                    "Тариф на этот участок не задан: дни участка дают нулевое начисление.",
                    false,
                    definition.Tiers));
                continue;
            }

            var segmentContributions = contributions
                .Where(item => item.DefinitionIndex == definitionIndex)
                .OrderBy(item => item.Band.From)
                .ToList();
            var amount = segmentContributions.Sum(item => item.Amount);
            var allocatedQuantity = MoneyMath.RoundMeterValue(segmentContributions.Sum(item => item.EffectiveQuantity));
            var mode = definition.CalculationBase switch
            {
                TariffCalculationBases.Fixed => "fixed",
                TariffCalculationBases.People => "people",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity when definition.Tiers.Count > 0 => "metered_tiered",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity => "metered",
                _ => "no_tariff"
            };
            var tierLines = mode == "metered_tiered"
                ? segmentContributions.Select(item => new AccrualCalculationTierDto(
                    item.Band.From,
                    item.Band.To,
                    MoneyMath.RoundMeterValue(item.EffectiveQuantity),
                    item.Rate,
                    item.Amount)).ToArray()
                : [];
            lines.Add(new AccrualCalculationLineDto(
                definition.EffectiveFrom,
                definition.EffectiveTo,
                days,
                monthDays,
                definition.CalculationBase,
                mode,
                definition.UnitName,
                definition.Rate,
                allocatedQuantity,
                amount,
                tierLines,
                BuildSegmentFormula(mode, monthlyQuantity, definition.Rate, days, monthDays, amount, segmentContributions),
                true,
                definition.Tiers));
        }

        var details = new AccrualCalculationDetailsDto(
            4,
            month,
            meterReading?.PreviousValue,
            meterReading?.CurrentValue,
            meterReading?.Consumption,
            requiresMeter,
            usesTieredTariff
                ? "Месячный расход распределяется по ступеням прогрессивно; объём каждой ступени умножается на ставки с учётом календарных дней их действия."
                : null,
            lines,
            total,
            averageRate,
            averageRate.HasValue
                ? BuildRateAveragingRule(activeDefinitions, monthDays, averageRate.Value, usesTieredTariff)
                : null,
            averageRate.HasValue
                ? BuildMonthlyCalculationFormula(
                    calculationBase!,
                    monthlyQuantity,
                    averageRate.Value,
                    total,
                    activeDefinitions[0].Definition.UnitName,
                    bands,
                    contributions,
                    activeDefinitions,
                    monthDays,
                    usesTieredTariff)
                : null);
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

    private static IReadOnlyList<ProgressiveBand> BuildProgressiveBands(
        decimal monthlyQuantity,
        IEnumerable<RegularAccrualSegmentDefinition> definitions)
    {
        var tieredDefinitions = definitions.Where(definition => definition.Tiers.Count > 0).ToList();
        if (tieredDefinitions.Count == 0)
        {
            return [new ProgressiveBand(0m, null, monthlyQuantity)];
        }

        var boundaries = tieredDefinitions
            .SelectMany(definition => definition.Tiers)
            .Where(tier => tier.UpperBound.HasValue && tier.UpperBound.Value > 0m)
            .Select(tier => tier.UpperBound!.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (monthlyQuantity == 0m)
        {
            return [new ProgressiveBand(0m, boundaries.FirstOrDefault(), 0m)];
        }
        var bands = new List<ProgressiveBand>();
        var lowerBound = 0m;
        foreach (var upperBound in boundaries)
        {
            var quantity = Math.Max(0m, Math.Min(monthlyQuantity, upperBound) - lowerBound);
            if (quantity > 0m)
            {
                bands.Add(new ProgressiveBand(lowerBound, upperBound, quantity));
            }
            lowerBound = upperBound;
        }

        if (monthlyQuantity > lowerBound || bands.Count == 0)
        {
            bands.Add(new ProgressiveBand(lowerBound, null, Math.Max(0m, monthlyQuantity - lowerBound)));
        }
        return bands;
    }

    private static IReadOnlyList<SegmentContribution> BuildContributions(
        IReadOnlyList<IndexedDefinition> definitions,
        IReadOnlyList<ProgressiveBand> bands,
        int monthDays)
    {
        var result = new List<SegmentContribution>();
        foreach (var band in bands)
        {
            var rawParts = definitions.Select(item =>
            {
                var days = item.Definition.EffectiveTo.DayNumber - item.Definition.EffectiveFrom.DayNumber + 1;
                var rate = ResolveTierRate(item.Definition, band);
                var effectiveQuantity = band.Quantity * days / monthDays;
                return new RawSegmentContribution(
                    item.Index,
                    band,
                    rate,
                    effectiveQuantity,
                    band.Quantity * rate * days / monthDays);
            }).ToList();
            var bandAmount = MoneyMath.RoundMoney(rawParts.Sum(item => item.RawAmount));
            var accumulatedAmount = 0m;
            for (var index = 0; index < rawParts.Count; index++)
            {
                var rawPart = rawParts[index];
                var amount = index == rawParts.Count - 1
                    ? MoneyMath.RoundMoney(bandAmount - accumulatedAmount)
                    : MoneyMath.RoundMoney(rawPart.RawAmount);
                accumulatedAmount += amount;
                result.Add(new SegmentContribution(
                    rawPart.DefinitionIndex,
                    rawPart.Band,
                    rawPart.Rate,
                    rawPart.EffectiveQuantity,
                    rawPart.RawAmount,
                    amount));
            }
        }
        return result;
    }

    private static decimal ResolveTierRate(RegularAccrualSegmentDefinition definition, ProgressiveBand band)
    {
        if (definition.Tiers.Count == 0)
        {
            return definition.Rate;
        }

        return definition.Tiers.FirstOrDefault(tier =>
            !tier.UpperBound.HasValue || band.From < tier.UpperBound.Value)?.Rate
            ?? definition.Tiers[^1].Rate;
    }

    private static string BuildRateAveragingRule(
        IReadOnlyList<IndexedDefinition> definitions,
        int monthDays,
        decimal averageRate,
        bool usesTieredTariff)
    {
        if (usesTieredTariff)
        {
            return $"Ставка каждой ступени взвешивается по календарным дням действия тарифов из {monthDays} дней месяца; дни без тарифа дают нулевое начисление.";
        }

        var parts = definitions.Select(item =>
        {
            var days = item.Definition.EffectiveTo.DayNumber - item.Definition.EffectiveFrom.DayNumber + 1;
            return $"{item.Definition.Rate.ToString("0.####", RussianCulture)} × {days}";
        });
        return $"Ставка за месяц с учётом календарных дней: ({string.Join(" + ", parts)}) / {monthDays} = {averageRate.ToString("0.####", RussianCulture)}. Дни без тарифа дают нулевое начисление.";
    }

    private static string BuildMonthlyCalculationFormula(
        string calculationBase,
        decimal monthlyQuantity,
        decimal averageRate,
        decimal amount,
        string unitName,
        IReadOnlyList<ProgressiveBand> bands,
        IReadOnlyList<SegmentContribution> contributions,
        IReadOnlyList<IndexedDefinition> definitions,
        int monthDays,
        bool usesTieredTariff)
    {
        if (usesTieredTariff)
        {
            var parts = bands.Select(band =>
            {
                var weightedRate = definitions.Sum(item =>
                {
                    var days = item.Definition.EffectiveTo.DayNumber - item.Definition.EffectiveFrom.DayNumber + 1;
                    return ResolveTierRate(item.Definition, band) * days / monthDays;
                });
                var bandAmount = contributions
                    .Where(item => item.Band == band)
                    .Sum(item => item.Amount);
                return $"{band.Quantity.ToString("0.###", RussianCulture)} × {weightedRate.ToString("0.####", RussianCulture)} = {bandAmount.ToString("0.00", RussianCulture)}";
            });
            return $"Прогрессивный расчёт за месяц: {string.Join("; ", parts)}; итого {amount.ToString("0.00", RussianCulture)}.";
        }

        var quantity = calculationBase switch
        {
            TariffCalculationBases.Fixed => "1 месяц",
            TariffCalculationBases.People => $"{monthlyQuantity.ToString("0.####", RussianCulture)} чел.",
            _ => $"{monthlyQuantity.ToString("0.###", RussianCulture)} {unitName}"
        };
        return $"Расчёт за месяц: {quantity} × {averageRate.ToString("0.####", RussianCulture)} = {amount.ToString("0.00", RussianCulture)}.";
    }

    private static string BuildSegmentFormula(
        string mode,
        decimal monthlyQuantity,
        decimal rate,
        int days,
        int monthDays,
        decimal amount,
        IReadOnlyList<SegmentContribution> contributions) => mode switch
        {
            "fixed" => $"1 месяц × {rate.ToString("0.####", RussianCulture)} × {days}/{monthDays} = {amount.ToString("0.00", RussianCulture)}",
            "people" => $"{monthlyQuantity.ToString("0.####", RussianCulture)} чел. × {rate.ToString("0.####", RussianCulture)} × {days}/{monthDays} = {amount.ToString("0.00", RussianCulture)}",
            "metered" => $"{monthlyQuantity.ToString("0.###", RussianCulture)} × {rate.ToString("0.####", RussianCulture)} × {days}/{monthDays} = {amount.ToString("0.00", RussianCulture)}",
            "metered_tiered" => $"Прогрессивные ступени × {days}/{monthDays}: {string.Join(" + ", contributions.Select(item => $"{item.Band.Quantity.ToString("0.###", RussianCulture)} × {item.Rate.ToString("0.####", RussianCulture)} × {days}/{monthDays}"))} = {amount.ToString("0.00", RussianCulture)}",
            _ => "Тариф на этот участок не задан: дни участка дают нулевое начисление."
        };

    private sealed record IndexedDefinition(int Index, RegularAccrualSegmentDefinition Definition);

    private sealed record ProgressiveBand(decimal From, decimal? To, decimal Quantity);

    private sealed record RawSegmentContribution(
        int DefinitionIndex,
        ProgressiveBand Band,
        decimal Rate,
        decimal EffectiveQuantity,
        decimal RawAmount);

    private sealed record SegmentContribution(
        int DefinitionIndex,
        ProgressiveBand Band,
        decimal Rate,
        decimal EffectiveQuantity,
        decimal RawAmount,
        decimal Amount);
}
