using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class RegularAccrualCalculatorTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    [Fact]
    public void Calculate_FixedRateChangeInsideMonth_UsesArithmeticMeanWithoutDayWeight()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [
                Segment(1, 15, TariffCalculationBases.Fixed, 310m),
                Segment(16, 31, TariffCalculationBases.Fixed, 620m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(465m, result.Amount);
        Assert.Equal(465m, result.Details!.AverageRate);
        Assert.Contains("(310 + 620) / 2 = 465", result.Details.RateAveragingRule, StringComparison.Ordinal);
        Assert.Contains("Количество дней действия ставок на среднее не влияет", result.Details.RateAveragingRule, StringComparison.Ordinal);
        Assert.Equal("Расчёт за месяц: 1 месяц × 465 = 465,00.", result.Details.MonthlyCalculationFormula);
        Assert.Collection(
            result.Details.Lines,
            line => Assert.Equal(155m, line.Amount),
            line => Assert.Equal(310m, line.Amount));
    }

    [Fact]
    public void Calculate_FourMeteredRates_UsesSimpleAverageForEntireMonthlyConsumption()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 108m),
            [
                Segment(1, 20, TariffCalculationBases.MeterWater, 1m),
                Segment(21, 25, TariffCalculationBases.MeterWater, 2m),
                Segment(26, 30, TariffCalculationBases.MeterWater, 3m),
                Segment(31, 31, TariffCalculationBases.MeterWater, 4m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(20m, result.Amount);
        Assert.Equal(2.5m, result.Details!.AverageRate);
        Assert.Equal("Средняя ставка за месяц: (1 + 2 + 3 + 4) / 4 = 2,5. Количество дней действия ставок на среднее не влияет.", result.Details.RateAveragingRule);
        Assert.Equal("Расчёт за месяц: 8 м³ × 2,5 = 20,00.", result.Details.MonthlyCalculationFormula);
        Assert.Null(result.Details.VolumeAllocationRule);
        Assert.All(result.Details.Lines, line => Assert.Equal(2m, line.Quantity));
        Assert.Equal([2m, 4m, 6m, 8m], result.Details.Lines.Select(line => line.Amount));
    }

    [Fact]
    public void Calculate_PeopleRates_UsesAverageRateForAllPeople()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [
                Segment(1, 30, TariffCalculationBases.People, 100m),
                Segment(31, 31, TariffCalculationBases.People, 200m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(300m, result.Amount);
        Assert.Equal(150m, result.Details!.AverageRate);
        Assert.Equal("Расчёт за месяц: 2 чел. × 150 = 300,00.", result.Details.MonthlyCalculationFormula);
    }

    [Fact]
    public void Calculate_ChangedCalculationBase_ReturnsFailureInsteadOfAveragingDifferentUnits()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 31m),
            [
                Segment(1, 15, TariffCalculationBases.Fixed, 310m),
                Segment(16, 31, TariffCalculationBases.MeterWater, 10m)
            ]);

        Assert.False(result.Succeeded);
        Assert.Contains("разными единицами нельзя усреднить", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_TieredSegment_SelectsRateByCurrentMeterValueForEntireConsumption()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 17m),
            [Segment(1, 31, TariffCalculationBases.MeterWater, 0m,
                new RegularAccrualTariffTier(10m, 5m),
                new RegularAccrualTariffTier(null, 8m))]);

        Assert.True(result.Succeeded);
        Assert.Equal(136m, result.Amount);
        var appliedTier = Assert.Single(result.Details!.Lines.Single().Tiers);
        Assert.Equal(10m, appliedTier.From);
        Assert.Null(appliedTier.To);
        Assert.Equal(17m, appliedTier.Quantity);
        Assert.Equal(8m, appliedTier.Rate);
        Assert.Equal(136m, appliedTier.Amount);
        Assert.Contains("текущее показание 17", result.Details.Lines.Single().Formula, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_TieredSegment_UsesLowerRateAtInclusiveThreshold()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 10m),
            [Segment(1, 31, TariffCalculationBases.MeterWater, 0m,
                new RegularAccrualTariffTier(10m, 5m),
                new RegularAccrualTariffTier(null, 8m))]);

        Assert.True(result.Succeeded);
        Assert.Equal(50m, result.Amount);
        var appliedTier = Assert.Single(result.Details!.Lines.Single().Tiers);
        Assert.Equal(0m, appliedTier.From);
        Assert.Equal(10m, appliedTier.To);
        Assert.Equal(5m, appliedTier.Rate);
    }

    [Fact]
    public void Calculate_MissingTariffSegment_ExplainsZeroAmount()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [
                Segment(1, 10, TariffCalculationBases.Fixed, 310m),
                Segment(11, 31, null, 0m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(310m, result.Amount);
        Assert.Equal(310m, result.Details!.AverageRate);
        Assert.Equal("Расчёт за месяц: 1 месяц × 310 = 310,00.", result.Details.MonthlyCalculationFormula);
        Assert.False(result.Details.Lines[1].HasTariff);
        Assert.Equal("no_tariff", result.Details.Lines[1].CalculationMode);
        Assert.Contains("в среднюю ставку месяца не входит", result.Details.Lines[1].Formula, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_MeteredSegmentWithoutReading_ReturnsFailure()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [Segment(1, 31, TariffCalculationBases.MeterWater, 10m)]);

        Assert.False(result.Succeeded);
        Assert.Contains("нет показания", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_RoundTripsAndRecalculatesWithNewReading()
    {
        var original = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 110m),
            [Segment(1, 31, TariffCalculationBases.MeterWater, 12m)]);

        var restored = RegularAccrualCalculator.Deserialize(RegularAccrualCalculator.Serialize(original.Details!));
        var recalculated = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 120m),
            RegularAccrualCalculator.FromSnapshot(restored!));

        Assert.Equal(120m, original.Amount);
        Assert.Equal(240m, recalculated.Amount);
        Assert.Equal(100m, recalculated.Details!.PreviousMeterValue);
        Assert.Equal(120m, recalculated.Details.CurrentMeterValue);
    }

    [Fact]
    public void TieredSnapshot_PreservesWholeGridWhenReadingMovesToAnotherThreshold()
    {
        var definitions = new[]
        {
            Segment(1, 31, TariffCalculationBases.MeterWater, 0m,
                new RegularAccrualTariffTier(10m, 5m),
                new RegularAccrualTariffTier(null, 8m))
        };
        var original = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 10m),
            definitions);

        var restored = RegularAccrualCalculator.Deserialize(RegularAccrualCalculator.Serialize(original.Details!));
        var recalculated = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 17m),
            RegularAccrualCalculator.FromSnapshot(restored!));

        Assert.Equal(50m, original.Amount);
        Assert.Equal(136m, recalculated.Amount);
        Assert.Equal(2, restored!.Lines.Single().TierDefinitions!.Count);
    }

    private static Garage Garage() => new() { Number = "1", PeopleCount = 2 };

    private static MeterReading Reading(decimal previous, decimal current) => new()
    {
        Garage = Garage(),
        MeterKind = MeterKinds.Water,
        AccountingMonth = August,
        ReadingDate = new DateOnly(2026, 8, 31),
        PreviousValue = previous,
        CurrentValue = current,
        Consumption = current - previous
    };

    private static RegularAccrualSegmentDefinition Segment(
        int fromDay,
        int toDay,
        string? calculationBase,
        decimal rate,
        params RegularAccrualTariffTier[] tiers) =>
        new(
            new DateOnly(2026, 8, fromDay),
            new DateOnly(2026, 8, toDay),
            calculationBase,
            rate,
            calculationBase == TariffCalculationBases.MeterWater ? "м³" : "руб.",
            tiers);
}
