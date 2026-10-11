using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterBaselineResolverTests
{
    [Fact]
    public void FindBaselineDevice_ReturnsNullWithoutDevices()
    {
        Assert.Null(MeterBaselineResolver.FindBaselineDevice([], null));
    }

    [Fact]
    public void FindBaselineDevice_UsesRestartBoundaryWhenThereAreNoActiveReadings()
    {
        var legacy = Device("Без номера", 1);
        var boundary = Device("Начало учёта 01.10.2026", 10);
        var replacement = Device("1", 20);

        Assert.Same(boundary, MeterBaselineResolver.FindBaselineDevice([legacy, boundary, replacement], null));
        Assert.Same(legacy, MeterBaselineResolver.FindBaselineDevice([legacy, replacement], null));
    }

    [Fact]
    public void FindBaselineDevice_UsesDeviceOfFirstReading()
    {
        var first = Device("A", 1);
        var second = Device("B", 20);

        var baseline = MeterBaselineResolver.FindBaselineDevice([first, second], Reading(second, replacement: false));

        Assert.Same(second, baseline);
    }

    [Fact]
    public void FindBaselineDevice_MovesToRemovedDeviceWhenFirstReadingIsReplacement()
    {
        var legacy = Device("Без номера", 1);
        var boundary = Device("Начало учёта 01.10.2026", 10);
        var replacement = Device("1", 20);

        var baseline = MeterBaselineResolver.FindBaselineDevice([legacy, boundary, replacement], Reading(replacement, replacement: true));

        Assert.Same(boundary, baseline);
    }

    [Fact]
    public void FindBaselineDevice_ReturnsNullForLegacyReadingWithoutDevice()
    {
        var device = Device("A", 1);

        Assert.Null(MeterBaselineResolver.FindBaselineDevice([device], new MeterReading { MeterKind = MeterKinds.Water, MeterDeviceId = null }));
        Assert.Null(MeterBaselineResolver.FindBaselineDevice([device], new MeterReading { MeterKind = MeterKinds.Water, MeterDeviceId = Guid.NewGuid() }));
    }

    private static MeterDevice Device(string serial, int day) =>
        new() { MeterKind = MeterKinds.Electricity, SerialNumber = serial, InstalledOn = new DateOnly(2026, 9, 1).AddDays(day) };

    private static MeterReading Reading(MeterDevice device, bool replacement) =>
        new() { MeterKind = MeterKinds.Electricity, MeterDeviceId = device.Id, IsMeterReplacement = replacement };
}
