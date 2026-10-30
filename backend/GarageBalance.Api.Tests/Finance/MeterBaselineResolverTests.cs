using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterBaselineResolverTests
{
    [Fact]
    public void AccountingRestartAfterAnOlderDevicePreservesItsIndependentBoundary()
    {
        var old = Device("OLD", 1);
        old.RemovedOn = new(2026, 9, 30);
        var restart = Device("Начало учёта 01.10.2026", 30);
        Assert.Equal(restart.InstalledOn, MeterBaselineResolver.GetEffectiveStartDate(restart, new(2026, 1, 1), [old, restart]));
        Assert.False(MeterBaselineResolver.IsGeneratedOpening(restart, [old, restart]));
    }

    [Fact]
    public void GeneratedOpening_PreservesTheRecordedDayWhenItsMonthAlreadyAgrees()
    {
        var device = Device("Начало учёта", 9);
        Assert.Equal(device.InstalledOn, MeterBaselineResolver.GetEffectiveStartDate(device, new(2026, 9, 1)));
    }

    [Theory]
    [InlineData("Начало учёта 01.10.2026", 7)]
    [InlineData("Без номера", 7)]
    [InlineData("Начало учёта 01.08.2026", 8)]
    public void GeneratedOpening_FollowsGarageAccountingMonth(string serial, int startMonth)
    {
        var device = Device(serial, 31);
        var storedDate = device.InstalledOn;
        Assert.Equal(new DateOnly(2026, startMonth, 1), MeterBaselineResolver.GetEffectiveStartDate(device, new(2026, startMonth, 1)));
        Assert.Equal(storedDate, device.InstalledOn);
    }

    [Fact]
    public void PhysicalReplacement_KeepsItsOwnBoundaryEvenWithAnUnnumberedSerial()
    {
        var device = Device("Без номера", 31);
        device.ReplacedDeviceId = Guid.NewGuid();
        device.ReplacementAccountingMonth = new(2026, 10, 1);
        Assert.Equal(device.InstalledOn, MeterBaselineResolver.GetEffectiveStartDate(device, new(2026, 7, 1)));
        Assert.Equal(device.InstalledOn, MeterBaselineResolver.GetEffectiveStartDate(Device("PHYSICAL", 31), new(2026, 7, 1)));
    }

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
