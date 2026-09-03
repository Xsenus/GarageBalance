using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class RegularAccrualCalculatorTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    [Fact]
    public void Calculate_FixedRateChangeInsideMonth_WeightsRatesByCalendarDays()
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
        Assert.Equal(470m, result.Amount);
        Assert.Equal(470m, result.Details!.AverageRate);
        Assert.Contains("(310 × 15 + 620 × 16) / 31 = 470", result.Details.RateAveragingRule, StringComparison.Ordinal);
        Assert.Equal("Расчёт за месяц: 1 месяц × 470 = 470,00.", result.Details.MonthlyCalculationFormula);
        Assert.Collection(
            result.Details.Lines,
            line => Assert.Equal(150m, line.Amount),
            line => Assert.Equal(320m, line.Amount));
    }

    [Fact]
    public void Calculate_FourMeteredRates_WeightsEveryRateByItsActiveDays()
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
        Assert.Equal(12.65m, result.Amount);
        Assert.Equal(1.5806m, result.Details!.AverageRate);
        Assert.Equal("Ставка за месяц с учётом календарных дней: (1 × 20 + 2 × 5 + 3 × 5 + 4 × 1) / 31 = 1,5806. Дни без тарифа дают нулевое начисление.", result.Details.RateAveragingRule);
        Assert.Equal("Расчёт за месяц: 8 м³ × 1,5806 = 12,65.", result.Details.MonthlyCalculationFormula);
        Assert.Null(result.Details.VolumeAllocationRule);
        Assert.Equal([5.161m, 1.29m, 1.29m, 0.258m], result.Details.Lines.Select(line => line.Quantity));
        Assert.Equal([5.16m, 2.58m, 3.87m, 1.04m], result.Details.Lines.Select(line => line.Amount));
    }

    [Fact]
    public void Calculate_PeopleRates_UsesDayWeightedRateForAllPeople()
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
        Assert.Equal(206.45m, result.Amount);
        Assert.Equal(103.2258m, result.Details!.AverageRate);
        Assert.Equal("Расчёт за месяц: 2 чел. × 103,2258 = 206,45.", result.Details.MonthlyCalculationFormula);
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
    public void Calculate_TieredSegment_SplitsMonthlyConsumptionProgressively()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 17m),
            [Segment(1, 31, TariffCalculationBases.MeterWater, 0m,
                new RegularAccrualTariffTier(10m, 5m),
                new RegularAccrualTariffTier(null, 8m))]);

        Assert.True(result.Succeeded);
        Assert.Equal(106m, result.Amount);
        Assert.Collection(
            result.Details!.Lines.Single().Tiers,
            tier =>
            {
                Assert.Equal(0m, tier.From);
                Assert.Equal(10m, tier.To);
                Assert.Equal(10m, tier.Quantity);
                Assert.Equal(5m, tier.Rate);
                Assert.Equal(50m, tier.Amount);
            },
            tier =>
            {
                Assert.Equal(10m, tier.From);
                Assert.Null(tier.To);
                Assert.Equal(7m, tier.Quantity);
                Assert.Equal(8m, tier.Rate);
                Assert.Equal(56m, tier.Amount);
            });
        Assert.Contains("Прогрессивные ступени", result.Details.Lines.Single().Formula, StringComparison.Ordinal);
    }

    [Fact]
    public void Calculate_TieredSegment_UsesOnlyLowerTierAtInclusiveThreshold()
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
        Assert.Equal(100m, result.Amount);
        Assert.Equal(100m, result.Details!.AverageRate);
        Assert.Equal("Расчёт за месяц: 1 месяц × 100 = 100,00.", result.Details.MonthlyCalculationFormula);
        Assert.False(result.Details.Lines[1].HasTariff);
        Assert.Equal("no_tariff", result.Details.Lines[1].CalculationMode);
        Assert.Contains("нулевое начисление", result.Details.Lines[1].Formula, StringComparison.Ordinal);
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
    public void Calculate_NegativeMeterConsumption_ReturnsFailure()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 90m),
            [Segment(1, 31, TariffCalculationBases.MeterWater, 10m)]);

        Assert.False(result.Succeeded);
        Assert.Contains("отрицательным", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_OverlappingTariffSegments_ReturnsFailure()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [
                Segment(1, 20, TariffCalculationBases.Fixed, 100m),
                Segment(20, 31, TariffCalculationBases.Fixed, 200m)
            ]);

        Assert.False(result.Succeeded);
        Assert.Contains("пересекаются", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(106m, recalculated.Amount);
        Assert.Equal(2, restored!.Lines.Single().TierDefinitions!.Count);
    }

    [Fact]
    public void Calculate_CustomerSeptemberThresholdExample_Returns17755()
    {
        var september = new DateOnly(2026, 9, 1);
        var reading = new MeterReading
        {
            Garage = Garage(),
            MeterKind = MeterKinds.Electricity,
            AccountingMonth = september,
            ReadingDate = new DateOnly(2026, 9, 30),
            PreviousValue = 0m,
            CurrentValue = 2000m,
            Consumption = 2000m
        };
        var oldTiers = new[]
        {
            new RegularAccrualTariffTier(1100m, 7.5m),
            new RegularAccrualTariffTier(1700m, 7.5m),
            new RegularAccrualTariffTier(null, 7.5m)
        };
        var newTiers = new[]
        {
            new RegularAccrualTariffTier(1100m, 7.5m),
            new RegularAccrualTariffTier(1700m, 10m),
            new RegularAccrualTariffTier(null, 12m)
        };

        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            september,
            reading,
            [
                new RegularAccrualSegmentDefinition(
                    new DateOnly(2026, 9, 1),
                    new DateOnly(2026, 9, 1),
                    TariffCalculationBases.MeterElectricity,
                    7.5m,
                    "кВт·ч",
                    oldTiers),
                new RegularAccrualSegmentDefinition(
                    new DateOnly(2026, 9, 2),
                    new DateOnly(2026, 9, 30),
                    TariffCalculationBases.MeterElectricity,
                    7.5m,
                    "кВт·ч",
                    newTiers)
            ]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(17755m, result.Amount);
        Assert.Equal(4, result.Details!.Version);
        Assert.Equal(8.8775m, result.Details.AverageRate);
        Assert.Contains("1100 × 7,5 = 8250,00", result.Details.MonthlyCalculationFormula, StringComparison.Ordinal);
        Assert.Contains("600 × 9,9167 = 5950,00", result.Details.MonthlyCalculationFormula, StringComparison.Ordinal);
        Assert.Contains("300 × 11,85 = 3555,00", result.Details.MonthlyCalculationFormula, StringComparison.Ordinal);
        Assert.Equal(17755m, result.Details.Lines.Sum(line => line.Amount));
        Assert.All(result.Details.Lines, line => Assert.Equal(3, line.Tiers.Count));
    }

    [Theory]
    [InlineData(2025, 2, 28)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 8, 31)]
    public void Calculate_RateChangeOnSecondDay_UsesActualMonthLength(int year, int monthNumber, int monthDays)
    {
        var month = new DateOnly(year, monthNumber, 1);
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            month,
            null,
            [
                Segment(month, 1, 1, TariffCalculationBases.Fixed, 100m),
                Segment(month, 2, monthDays, TariffCalculationBases.Fixed, 200m)
            ]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(MoneyMath.RoundMoney((100m + 200m * (monthDays - 1)) / monthDays), result.Amount);
        Assert.Equal(monthDays, result.Details!.Lines.Sum(line => line.Days));
    }

    [Fact]
    public void Calculate_RateChangeOnFirstDay_UsesNewRateForWholeMonth()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [Segment(August, 1, 31, TariffCalculationBases.Fixed, 200m)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(200m, result.Amount);
        Assert.Equal(200m, result.Details!.AverageRate);
    }

    [Fact]
    public void Calculate_RateChangeOnLastDay_WeightsOnlyLastDayAtNewRate()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            null,
            [
                Segment(August, 1, 30, TariffCalculationBases.Fixed, 100m),
                Segment(August, 31, 31, TariffCalculationBases.Fixed, 200m)
            ]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(103.23m, result.Amount);
        Assert.Equal([96.77m, 6.46m], result.Details!.Lines.Select(line => line.Amount));
    }

    [Fact]
    public void Calculate_ThreeRatePeriods_WeightsAllChanges()
    {
        var september = new DateOnly(2026, 9, 1);
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            september,
            null,
            [
                Segment(september, 1, 10, TariffCalculationBases.Fixed, 300m),
                Segment(september, 11, 20, TariffCalculationBases.Fixed, 600m),
                Segment(september, 21, 30, TariffCalculationBases.Fixed, 900m)
            ]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(600m, result.Amount);
        Assert.Equal([100m, 200m, 300m], result.Details!.Lines.Select(line => line.Amount));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 100)]
    [InlineData(75, 175)]
    [InlineData(100, 250)]
    [InlineData(130, 400)]
    public void Calculate_ProgressiveTiers_HandleZeroBoundariesAndOpenTier(decimal consumption, decimal expectedAmount)
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 100m + consumption),
            [Segment(1, 31, TariffCalculationBases.MeterElectricity, 2m,
                new RegularAccrualTariffTier(50m, 2m),
                new RegularAccrualTariffTier(100m, 3m),
                new RegularAccrualTariffTier(null, 5m))]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(expectedAmount, result.Amount);
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

    private static RegularAccrualSegmentDefinition Segment(
        DateOnly month,
        int fromDay,
        int toDay,
        string? calculationBase,
        decimal rate,
        params RegularAccrualTariffTier[] tiers) =>
        new(
            new DateOnly(month.Year, month.Month, fromDay),
            new DateOnly(month.Year, month.Month, toDay),
            calculationBase,
            rate,
            calculationBase switch
            {
                TariffCalculationBases.MeterWater => "м³",
                TariffCalculationBases.MeterElectricity => "кВт·ч",
                _ => "руб."
            },
            tiers);
}
