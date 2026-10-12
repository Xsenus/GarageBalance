using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterReplacementSegmentsTests
{
    [Theory]
    [InlineData(100, 115, 5, 9, 20, 19)]
    [InlineData(100, 100, 5, 5, 20, 0)]
    [InlineData(100.001, 100.003, 5.001, 5.004, 20, 0.005)]
    public void CalculatesAllSegmentsAndPreservesTheFirstBaseline(
        double baseline, double firstFinal, double secondInitial, double secondFinal, double thirdInitial, double expected)
    {
        var first = Device("A", new DateOnly(2026, 5, 1), (decimal)baseline, (decimal)firstFinal);
        var second = Device("B", new DateOnly(2026, 6, 10), (decimal)secondInitial, (decimal)secondFinal);
        var third = Device("C", new DateOnly(2026, 6, 20), (decimal)thirdInitial, null);
        second.ReplacedDeviceId = first.Id;
        third.ReplacedDeviceId = second.Id;
        var reading = Reading(third);
        Assert.Equal((decimal)expected, MeterReplacementSegments.CalculateCarry(reading, null, [first, second, third]));
        Assert.Same(first, MeterBaselineResolver.FindBaselineDevice([first, second, third], reading));
        var previous = new MeterReading { MeterKind = MeterKinds.Electricity, MeterDeviceId = first.Id, CurrentValue = (decimal)baseline + 1m };
        var result = MeterReplacementSegments.CalculateCarry(reading, previous, [first, second, third]);
        Assert.Equal((decimal)firstFinal < previous.CurrentValue ? null : (decimal)expected - 1m, result);
    }

    [Fact]
    public void RejectsNegativeSegmentsMissingFinalAndCycles()
    {
        var first = Device("A", new DateOnly(2026, 5, 1), 10m, 9m);
        var second = Device("B", new DateOnly(2026, 6, 10), 0m, null);
        second.ReplacedDeviceId = first.Id;
        Assert.Null(MeterReplacementSegments.CalculateCarry(Reading(second), null, [first, second]));
        first.FinalValue = null;
        Assert.Null(MeterReplacementSegments.CalculateCarry(Reading(second), null, [first, second]));
        first.FinalValue = 15m;
        first.InstalledOn = new DateOnly(2026, 6, 5);
        first.ReplacedDeviceId = second.Id;
        second.FinalValue = 1m;
        Assert.Null(MeterReplacementSegments.CalculateCarry(Reading(second), null, [first, second]));
    }

    [Fact]
    public void LegacyFallbackAndCanceledDevicesDoNotChangeOrdinaryReadings()
    {
        var first = Device("A", new DateOnly(2026, 5, 1), 10m, 15m);
        var second = Device("B", new DateOnly(2026, 6, 10), 0m, null);
        first.RemovedOn = new DateOnly(2026, 6, 9);
        var reading = Reading(second);
        reading.PreviousDeviceConsumption = 42m;
        Assert.Equal(5m, MeterReplacementSegments.CalculateCarry(reading, null, [first, second]));
        first.IsCanceled = true;
        Assert.Equal(42m, MeterReplacementSegments.CalculateCarry(reading, null, [first, second]));
        reading.IsMeterReplacement = false;
        Assert.Equal(42m, MeterReplacementSegments.CalculateCarry(reading, null, [first, second]));
        reading.MeterDevice = null;
        reading.IsMeterReplacement = true;
        Assert.Equal(42m, MeterReplacementSegments.CalculateCarry(reading, null, []));
    }

    private static MeterDevice Device(string serial, DateOnly installed, decimal initial, decimal? final) =>
        new() { MeterKind = MeterKinds.Electricity, SerialNumber = serial, InstalledOn = installed, InitialValue = initial, FinalValue = final };
    private static MeterReading Reading(MeterDevice device) =>
        new() { MeterKind = MeterKinds.Electricity, AccountingMonth = new DateOnly(2026, 6, 1), MeterDeviceId = device.Id, MeterDevice = device, IsMeterReplacement = true };
}
