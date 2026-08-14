using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class RegularAccrualCalculatorTests
{
    private static readonly DateOnly August = new(2026, 8, 1);

    [Fact]
    public void Calculate_FixedRateChangeInsideMonth_ProreratesEverySegment()
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
        Assert.Collection(
            result.Details!.Lines,
            line => Assert.Equal(150m, line.Amount),
            line => Assert.Equal(320m, line.Amount));
    }

    [Fact]
    public void Calculate_FixedToMetered_ProreratesRateAndConsumptionByCalendarDays()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 131m),
            [
                Segment(1, 15, TariffCalculationBases.Fixed, 310m),
                Segment(16, 31, TariffCalculationBases.MeterWater, 10m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(310m, result.Amount);
        Assert.Equal(16m, result.Details!.Lines[1].Quantity);
        Assert.Equal(160m, result.Details.Lines[1].Amount);
        Assert.NotNull(result.Details.VolumeAllocationRule);
    }

    [Fact]
    public void Calculate_MeteredToFixed_UsesTheHistoricalOrderOfModes()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 100m, current: 131m),
            [
                Segment(1, 15, TariffCalculationBases.MeterWater, 10m),
                Segment(16, 31, TariffCalculationBases.Fixed, 310m)
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(310m, result.Amount);
        Assert.Equal(15m, result.Details!.Lines[0].Quantity);
        Assert.Equal(150m, result.Details.Lines[0].Amount);
        Assert.Equal(160m, result.Details.Lines[1].Amount);
    }

    [Fact]
    public void Calculate_MultipleModeTransitions_CombinesFixedMeteredAndTieredSegments()
    {
        var result = RegularAccrualCalculator.Calculate(
            Garage(),
            August,
            Reading(previous: 0m, current: 31m),
            [
                Segment(1, 5, TariffCalculationBases.Fixed, 310m),
                Segment(6, 10, TariffCalculationBases.MeterWater, 10m),
                Segment(11, 20, TariffCalculationBases.Fixed, 310m),
                Segment(21, 31, TariffCalculationBases.MeterWater, 0m,
                    new RegularAccrualTariffTier(5m, 10m),
                    new RegularAccrualTariffTier(null, 20m))
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(420m, result.Amount);
        Assert.Equal(new[] { "fixed", "metered", "fixed", "metered_tiered" }, result.Details!.Lines.Select(line => line.CalculationMode));
        Assert.Single(result.Details.Lines[3].Tiers);
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
        Assert.Equal(100m, result.Amount);
        Assert.False(result.Details!.Lines[1].HasTariff);
        Assert.Equal("no_tariff", result.Details.Lines[1].CalculationMode);
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
