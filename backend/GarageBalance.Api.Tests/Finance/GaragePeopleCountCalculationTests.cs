using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GaragePeopleCountCalculationTests
{
    [Theory]
    [InlineData(2026, 2)]
    [InlineData(2028, 2)]
    [InlineData(2026, 4)]
    [InlineData(2026, 8)]
    public void PeopleChangeOnThirteenthUsesExactCalendarDays(int year, int monthNumber)
    {
        var month = new DateOnly(year, monthNumber, 1);
        var garage = new Garage { Number = "DAILY", PeopleCount = 2 };
        var days = DateTime.DaysInMonth(year, monthNumber);
        var result = RegularAccrualCalculator.Calculate(garage, month, null,
            [Segment(month, month.AddMonths(1).AddDays(-1), 130m)],
            [Period(garage, month, 1), Period(garage, month.AddDays(12), 2)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(MoneyMath.RoundMoney(130m * (12 + 2 * (days - 12)) / days), result.Amount);
        Assert.False(result.Details!.RequiresMeter);
        Assert.Equal(5, result.Details.Version);
        Assert.Equal([12, days - 12], result.Details.Lines.Select(line => line.Days));
        Assert.Equal([1, 2], result.Details.Lines.Select(line => line.PeopleCount!.Value));
        Assert.Equal(result.Amount, result.Details.Lines.Sum(line => line.Amount));
        if (days == 30)
        {
            Assert.Equal(208m, result.Amount);
            Assert.Contains("Среднее число людей за месяц: 1,6", result.Details.MonthlyCalculationFormula, StringComparison.Ordinal);
        }

        garage.PeopleCount = 99;
        var snapshot = RegularAccrualCalculator.Deserialize(RegularAccrualCalculator.Serialize(result.Details));
        var replay = RegularAccrualCalculator.Calculate(garage, month, null, RegularAccrualCalculator.FromSnapshot(snapshot!));
        Assert.True(replay.Succeeded, replay.ErrorMessage);
        Assert.Equal(result.Amount, replay.Amount);
        Assert.Equal(result.Details.MonthlyCalculationFormula, replay.Details!.MonthlyCalculationFormula);
    }

    [Fact]
    public void PeopleAndTariffChangesAreMultipliedWithinTheirOverlappingPeriods()
    {
        var month = new DateOnly(2026, 4, 1);
        var garage = new Garage { Number = "BOTH", PeopleCount = 2 };
        var result = RegularAccrualCalculator.Calculate(garage, month, null,
            [Segment(month, month.AddDays(19), 100m), Segment(month.AddDays(20), month.AddMonths(1).AddDays(-1), 200m)],
            [Period(garage, month.AddDays(12), 2), Period(garage, month.AddMonths(-1), 1)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(226.67m, result.Amount);
        Assert.Equal([12, 8, 10], result.Details!.Lines.Select(line => line.Days));
        Assert.Equal([1, 2, 2], result.Details.Lines.Select(line => line.PeopleCount!.Value));
        Assert.Equal([100m, 100m, 200m], result.Details.Lines.Select(line => line.Rate));
        Assert.Equal(result.Amount, result.Details.Lines.Sum(line => line.Amount));
        Assert.Null(result.Details.AverageRate);
    }

    [Fact]
    public void FirstLastAndNextMonthChangesRespectInclusiveDatesAndZeroPeople()
    {
        var month = new DateOnly(2026, 8, 1);
        var garage = new Garage { Number = "BOUNDARY", PeopleCount = 9 };
        var result = RegularAccrualCalculator.Calculate(garage, month, null,
            [Segment(month, month.AddMonths(1).AddDays(-1), 125m)],
            [Period(garage, month.AddDays(-1), 1), Period(garage, month, 0), Period(garage, month.AddDays(30), 2), Period(garage, month.AddMonths(1), 9)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(8.06m, result.Amount);
        Assert.Equal([30, 1], result.Details!.Lines.Select(line => line.Days));
        Assert.Equal([0, 2], result.Details.Lines.Select(line => line.PeopleCount!.Value));
    }

    [Fact]
    public void NoTariffDaysStayFreeAndCentDistributionNeverCreatesNegativeLines()
    {
        var month = new DateOnly(2026, 8, 1);
        var garage = new Garage { Number = "ROUNDING", PeopleCount = 0 };
        var definitions = Enumerable.Range(0, 3).Select(day => Segment(month.AddDays(day), month.AddDays(day), 0.19m))
            .Append(new RegularAccrualSegmentDefinition(month.AddDays(3), month.AddDays(30), null, 0, "чел.", [])).ToArray();
        var result = RegularAccrualCalculator.Calculate(garage, month, null, definitions,
            [Period(garage, month, 1), Period(garage, month.AddDays(3), 0)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0.02m, result.Amount);
        Assert.Equal([0.01m, 0.01m, 0m, 0m], result.Details!.Lines.Select(line => line.Amount));
        Assert.All(result.Details.Lines, line => Assert.True(line.Amount >= 0));
        Assert.False(result.Details.Lines[^1].HasTariff);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1001, false)]
    [InlineData(2, true)]
    public void InvalidHistoryFailsInsteadOfInventingACharge(int count, bool duplicateDate)
    {
        var month = new DateOnly(2026, 8, 1);
        var garage = new Garage { Number = "INVALID", PeopleCount = 1 };
        var periods = new List<GaragePeopleCountPeriod> { Period(garage, month, count) };
        if (duplicateDate) periods.Add(Period(garage, month, 3));
        var result = RegularAccrualCalculator.Calculate(garage, month, null, [Segment(month, month.AddDays(30), 130m)], periods);
        Assert.False(result.Succeeded);
        Assert.Null(result.Details);
    }

    [Theory]
    [InlineData(-1, 130)]
    [InlineData(1001, 130)]
    [InlineData(2, -1)]
    public void InvalidSnapshotCountOrRateFails(int count, int rate)
    {
        var month = new DateOnly(2026, 8, 1);
        var garage = new Garage { Number = "INVALID-SNAPSHOT", PeopleCount = 1 };
        var segment = Segment(month, month.AddDays(30), rate) with { PeopleCount = count };
        var result = RegularAccrualCalculator.Calculate(garage, month, null, [segment]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Details);
    }

    private static GaragePeopleCountPeriod Period(Garage garage, DateOnly from, int count) =>
        new() { GarageId = garage.Id, EffectiveFrom = from, PeopleCount = count };

    private static RegularAccrualSegmentDefinition Segment(DateOnly from, DateOnly to, decimal rate) =>
        new(from, to, TariffCalculationBases.People, rate, "чел.", []);
}
