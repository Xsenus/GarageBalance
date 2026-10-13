using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionCalculationTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    public void FirstMonth_ChargesAllMeasuredConsumptionWithoutSecondProration(int day)
    {
        var connection = Connection(September.AddDays(day - 1));
        var result = Calculate(connection, 23m, [Segment(1, 30, 5m)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(115m, result.Amount);
        Assert.Equal(23m, Assert.Single(result.Details!.Lines).Quantity);
        Assert.Equal(connection.ConnectedOn, result.Details.MeterConnectedOn);
        Assert.Equal(connection.ConnectedOn, result.Details.Lines[0].EffectiveFrom);
        Assert.Equal(31 - day, result.Details.Lines[0].Days);
        Assert.Equal(5, result.Details.Version);
    }

    [Fact]
    public void RateChange_WeightsOnlyConnectedDays_AndSnapshotRetainsDates()
    {
        var connection = Connection(September.AddDays(9));
        var result = Calculate(connection, 21m, [Segment(1, 19, 2m), Segment(20, 30, 4m)]);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(64m, result.Amount);
        Assert.Equal(new[] { 10m, 11m }, result.Details!.Lines.Select(line => line.Quantity));
        var snapshot = RegularAccrualCalculator.Deserialize(RegularAccrualCalculator.Serialize(result.Details));
        Assert.NotNull(snapshot);
        Assert.Equal(connection.ConnectedOn, snapshot.MeterConnectedOn);
        Assert.Equal(result.Amount, Calculate(connection, 21m, RegularAccrualCalculator.FromSnapshot(snapshot)).Amount);
    }

    [Fact]
    public void TieredRate_UsesActualConsumptionForBandsAndConnectedDaysForPriceWeights()
    {
        var definitions = new[] { Segment(1, 30, 0m) with { Tiers = [new(10m, 2m), new(null, 3m)] } };
        var result = Calculate(Connection(September.AddDays(9)), 23m, definitions);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(59m, result.Amount);
        Assert.Equal(new[] { 10m, 13m }, Assert.Single(result.Details!.Lines).Tiers.Select(tier => tier.Quantity));
    }

    [Fact]
    public void NextYear_DoesNotReusePartialFirstMonthDenominator()
    {
        var month = new DateOnly(2027, 1, 1);
        var result = RegularAccrualCalculator.Calculate(new Garage { Number = "synthetic" }, month,
            Reading(month, 17m), [new(month, month.AddMonths(1).AddDays(-1), TariffCalculationBases.MeterElectricity, 5m, "кВт·ч", [])],
            meterConnection: Connection(new DateOnly(2026, 12, 20)));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(85m, result.Amount);
        Assert.Equal(31, Assert.Single(result.Details!.Lines).Days);
    }

    [Fact]
    public void MissingBase_ZeroConsumptionAndUnknownBaseRemainDifferent()
    {
        var connection = Connection(September.AddDays(9));
        Assert.Equal(0m, Calculate(connection, 0m, [Segment(1, 30, 5m)]).Amount);
        connection.BaselineMeterDeviceId = null;
        var result = Calculate(connection, 0m, [Segment(1, 30, 5m)]);
        Assert.False(result.Succeeded);
        Assert.Contains("стартовое показание", result.ErrorMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutsideConnectionPeriod_IsRejected(bool future)
    {
        var connection = Connection(future ? September.AddMonths(1) : September.AddMonths(-2));
        if (!future) connection.ClosedOn = September.AddDays(-1);
        var result = Calculate(connection, 23m, [Segment(1, 30, 5m)]);
        Assert.False(result.Succeeded);
        Assert.Contains("не подключена", result.ErrorMessage);
    }

    [Fact]
    public void MissingPriceDuringConnectedDays_IsRejectedButEarlierMissingPriceIsIrrelevant()
    {
        var connection = Connection(September.AddDays(9));
        Assert.True(Calculate(connection, 23m, [Segment(10, 30, 5m)]).Succeeded);
        Assert.False(Calculate(connection, 23m, [Segment(11, 30, 5m)]).Succeeded);
        Assert.False(Calculate(connection, 23m, [Segment(1, 14, 5m), Segment(16, 30, 5m)]).Succeeded);
        Assert.False(Calculate(connection, 23m, [Segment(1, 30, 5m) with { CalculationBase = null }, Segment(20, 30, 5m)]).Succeeded);
        Assert.False(Calculate(connection, 23m, [Segment(1, 30, 5m) with { CalculationBase = null }]).Succeeded);
        Assert.False(Calculate(connection, 23m, []).Succeeded);
    }

    [Fact]
    public void LegacyWithoutConnection_PreservesOriginalCalculationAndSerializedContract()
    {
        var result = Calculate(null, 30m, [Segment(10, 30, 5m)]);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(105m, result.Amount);
        Assert.Equal(4, result.Details!.Version);
        Assert.DoesNotContain("meterConnectedOn", RegularAccrualCalculator.Serialize(result.Details));
    }

    private static GarageMeterConnection Connection(DateOnly from) => new()
    {
        MeterKind = MeterKinds.Electricity,
        ConnectedOn = from,
        BaselineMeterDeviceId = Guid.NewGuid()
    };

    private static MeterReading Reading(DateOnly month, decimal consumption) => new()
    {
        MeterKind = MeterKinds.Electricity,
        AccountingMonth = month,
        ReadingDate = month.AddMonths(1).AddDays(-1),
        PreviousValue = 250m,
        CurrentValue = 250m + consumption,
        Consumption = consumption
    };

    private static RegularAccrualSegmentDefinition Segment(int from, int to, decimal rate) =>
        new(September.AddDays(from - 1), September.AddDays(to - 1), TariffCalculationBases.MeterElectricity, rate, "кВт·ч", []);

    private static RegularAccrualCalculationResult Calculate(GarageMeterConnection? connection, decimal consumption,
        IReadOnlyList<RegularAccrualSegmentDefinition> definitions) =>
        RegularAccrualCalculator.Calculate(new Garage { Number = "synthetic" }, September, Reading(September, consumption),
            definitions, meterConnection: connection);
}
