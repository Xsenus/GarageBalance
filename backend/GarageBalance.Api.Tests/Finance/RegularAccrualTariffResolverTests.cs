using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Finance;

public sealed class RegularAccrualTariffResolverTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Fact]
    public void PartialIndividualPeriodOverridesGeneralChangesAndRestoresGeneralAfterwards()
    {
        var general = new[] { Segment(1, 10, 100m), Segment(11, 30, 200m) };
        var result = RegularAccrualTariffResolver.ApplyIndividualSegments(September, general, [Segment(5, 20, 300m)]);
        Assert.Equal([(1, 4, 100m), (5, 20, 300m), (21, 30, 200m)],
            result.Select(segment => (segment.EffectiveFrom.Day, segment.EffectiveTo.Day, segment.Rate)).ToArray());
        Assert.Equal(3, result.Count); // A general change hidden by the override must not split it.
        var calculation = RegularAccrualCalculator.Calculate(new Garage { Number = "85" }, September, null, result);
        Assert.True(calculation.Succeeded);
        Assert.Equal(240m, calculation.Amount);
        Assert.Equal(240m, calculation.Details!.AverageRate);
    }

    [Fact]
    public void IndividualTariffPreservesProgressiveTiersAndPeopleCount()
    {
        IReadOnlyList<RegularAccrualTariffTier> tiers = [new(100m, 5m), new(null, 10m)];
        var custom = Segment(1, 30, 5m) with { CalculationBase = TariffCalculationBases.MeterElectricity, Tiers = tiers, PeopleCount = 3 };
        var result = Assert.Single(RegularAccrualTariffResolver.ApplyIndividualSegments(September, [Segment(1, 30, 100m)], [custom]));
        Assert.Same(tiers, result.Tiers);
        Assert.Equal(3, result.PeopleCount);
        Assert.Equal(TariffCalculationBases.MeterElectricity, result.CalculationBase);
        Assert.Equal(custom.UnitName, result.UnitName);
    }

    [Fact]
    public void NoApplicableIndividualPeriodLeavesOriginalScheduleUnchanged()
    {
        IReadOnlyList<RegularAccrualSegmentDefinition> general = [Segment(1, 30, 100m)];
        Assert.Same(general, RegularAccrualTariffResolver.ApplyIndividualSegments(September, general, []));
        Assert.Same(general, RegularAccrualTariffResolver.ApplyIndividualSegments(September, general,
            [Segment(1, 30, 300m) with { EffectiveFrom = new(2026, 10, 1), EffectiveTo = new(2026, 10, 30) }]));
    }

    [Fact]
    public void IndividualPeriodIsClippedToMonthAndAppliesWhenGeneralTariffIsMissing()
    {
        var custom = Segment(1, 30, 100m) with { EffectiveFrom = new(2026, 8, 15), EffectiveTo = new(2026, 10, 15) };
        var result = Assert.Single(RegularAccrualTariffResolver.ApplyIndividualSegments(new(2026, 9, 18), [], [custom]));
        Assert.Equal(September, result.EffectiveFrom);
        Assert.Equal(new DateOnly(2026, 9, 30), result.EffectiveTo);
        Assert.Equal(100m, result.Rate);
    }

    [Fact]
    public void MissingGeneralDaysStayMissingAndAdjacentIndividualPeriodsAreAllowed()
    {
        var result = RegularAccrualTariffResolver.ApplyIndividualSegments(September, [], [Segment(5, 10, 100m), Segment(11, 15, 200m)]);
        Assert.Equal([(1, 4, (string?)null), (5, 10, TariffCalculationBases.Fixed), (11, 15, TariffCalculationBases.Fixed), (16, 30, (string?)null)],
            result.Select(segment => (segment.EffectiveFrom.Day, segment.EffectiveTo.Day, segment.CalculationBase)).ToArray());
        Assert.Equal([0m, 100m, 200m, 0m], result.Select(segment => segment.Rate).ToArray());
        Assert.All(result, segment => Assert.Equal("руб.", segment.UnitName));
    }

    [Theory]
    [InlineData(2024, 29)]
    [InlineData(2026, 28)]
    public void UsesActualCalendarMonthLength(int year, int days)
    {
        var month = new DateOnly(year, 2, 1);
        var custom = Segment(1, 30, 100m) with { EffectiveFrom = month.AddMonths(-1), EffectiveTo = month.AddMonths(1) };
        var result = Assert.Single(RegularAccrualTariffResolver.ApplyIndividualSegments(month, [], [custom]));
        Assert.Equal(days, result.EffectiveTo.Day);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsOverlappingSchedulesRatherThanSilentlyChoosingWrongRate(bool individualOverlap)
    {
        RegularAccrualSegmentDefinition[] overlaps = [Segment(1, 15, 100m), Segment(15, 30, 200m)];
        RegularAccrualSegmentDefinition[] valid = [Segment(1, 30, 300m)];
        Assert.Throws<ArgumentException>(() => RegularAccrualTariffResolver.ApplyIndividualSegments(September,
            individualOverlap ? valid : overlaps, individualOverlap ? overlaps : valid));
    }

    [Fact]
    public void RejectsReversedPeriodAndDoesNotMutateInput()
    {
        var general = Segment(1, 30, 100m);
        var custom = Segment(5, 15, 200m);
        _ = RegularAccrualTariffResolver.ApplyIndividualSegments(September, [general], [custom]);
        Assert.Equal((1, 30), (general.EffectiveFrom.Day, general.EffectiveTo.Day));
        Assert.Equal((5, 15), (custom.EffectiveFrom.Day, custom.EffectiveTo.Day));
        Assert.Throws<ArgumentException>(() => RegularAccrualTariffResolver.ApplyIndividualSegments(September, [general], [Segment(15, 5, 200m)]));
    }

    private static RegularAccrualSegmentDefinition Segment(int from, int to, decimal rate) =>
        new(new(2026, 9, from), new(2026, 9, to), TariffCalculationBases.Fixed, rate, "руб.", []);
}
