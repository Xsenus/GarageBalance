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
    bool HasTariff);

public sealed record AccrualCalculationDetailsDto(
    int Version,
    DateOnly AccountingMonth,
    decimal? PreviousMeterValue,
    decimal? CurrentMeterValue,
    decimal? MeterConsumption,
    bool RequiresMeter,
    string? VolumeAllocationRule,
    IReadOnlyList<AccrualCalculationLineDto> Lines,
    decimal TotalAmount);

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
        var requiresMeter = ordered.Any(IsMetered);
        if (requiresMeter && meterReading is null)
        {
            return RegularAccrualCalculationResult.Failure("нет показания счетчика за месяц.");
        }

        var lines = new List<AccrualCalculationLineDto>(ordered.Count);
        foreach (var definition in ordered)
        {
            var days = definition.EffectiveTo.DayNumber - definition.EffectiveFrom.DayNumber + 1;
            var dayShare = days / (decimal)monthDays;
            var calculationBase = definition.CalculationBase;
            var quantity = calculationBase switch
            {
                TariffCalculationBases.Fixed => dayShare,
                TariffCalculationBases.People => garage.PeopleCount * dayShare,
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity =>
                    MoneyMath.RoundMeterValue(meterReading!.Consumption * dayShare),
                _ => 0m
            };
            var tierLines = IsMetered(definition) && definition.Tiers.Count > 0
                ? CalculateTierLines(quantity, definition.Tiers)
                : [];
            var unroundedAmount = tierLines.Count > 0
                ? tierLines.Sum(tier => tier.Amount)
                : quantity * definition.Rate;
            var amount = MoneyMath.RoundMoney(unroundedAmount);
            var mode = calculationBase switch
            {
                TariffCalculationBases.Fixed => "fixed",
                TariffCalculationBases.People => "people",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity when definition.Tiers.Count > 0 => "metered_tiered",
                TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity => "metered",
                _ => "no_tariff"
            };
            lines.Add(new AccrualCalculationLineDto(
                definition.EffectiveFrom,
                definition.EffectiveTo,
                days,
                monthDays,
                calculationBase,
                mode,
                definition.UnitName,
                definition.Rate,
                quantity,
                amount,
                tierLines,
                BuildFormula(mode, quantity, definition.Rate, days, monthDays, amount),
                calculationBase is not null));
        }

        var total = MoneyMath.RoundMoney(lines.Sum(line => line.Amount));
        var details = new AccrualCalculationDetailsDto(
            1,
            month,
            meterReading?.PreviousValue,
            meterReading?.CurrentValue,
            meterReading?.Consumption,
            requiresMeter,
            requiresMeter && ordered.Count > 1
                ? "Расход за месяц распределён между участками пропорционально календарным дням их действия."
                : null,
            lines,
            total);
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
            line.Tiers.Select(tier => new RegularAccrualTariffTier(tier.To, tier.Rate)).ToArray()))
            .ToArray();

    private static bool IsMetered(RegularAccrualSegmentDefinition definition) =>
        definition.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;

    private static IReadOnlyList<AccrualCalculationTierDto> CalculateTierLines(
        decimal consumption,
        IReadOnlyList<RegularAccrualTariffTier> tiers)
    {
        var result = new List<AccrualCalculationTierDto>();
        var lowerBound = 0m;
        foreach (var tier in tiers)
        {
            var upperBound = tier.UpperBound ?? consumption;
            var quantity = Math.Max(Math.Min(consumption, upperBound) - lowerBound, 0m);
            if (quantity > 0m)
            {
                result.Add(new AccrualCalculationTierDto(
                    lowerBound == 0m ? 0m : lowerBound + 1m,
                    tier.UpperBound,
                    quantity,
                    tier.Rate,
                    MoneyMath.RoundMoney(quantity * tier.Rate)));
            }

            if (!tier.UpperBound.HasValue || consumption <= upperBound)
            {
                break;
            }

            lowerBound = upperBound;
        }

        return result;
    }

    private static string BuildFormula(string mode, decimal quantity, decimal rate, int days, int monthDays, decimal amount) => mode switch
    {
        "fixed" => $"{rate.ToString("0.####", RussianCulture)} × {days}/{monthDays} = {amount.ToString("0.00", RussianCulture)}",
        "people" => $"{rate.ToString("0.####", RussianCulture)} × {quantity.ToString("0.####", RussianCulture)} чел. = {amount.ToString("0.00", RussianCulture)}",
        "metered" => $"{quantity.ToString("0.###", RussianCulture)} × {rate.ToString("0.####", RussianCulture)} = {amount.ToString("0.00", RussianCulture)}",
        "metered_tiered" => $"{quantity.ToString("0.###", RussianCulture)} по пороговой сетке = {amount.ToString("0.00", RussianCulture)}",
        _ => "Тариф на этот участок не задан: 0,00"
    };
}
