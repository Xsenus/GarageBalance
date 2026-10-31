using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceServiceTests
{
    [Fact]
    public void Contract_RequiresRemovedDeviceFinalValue()
    {
        // ASP.NET validates positional records using their primary constructor metadata.
        var parameter = typeof(ReplaceMeterDeviceRequest).GetConstructors().Single()
            .GetParameters().Single(item => item.Name == nameof(ReplaceMeterDeviceRequest.RemovedDeviceFinalValue));
        var required = Assert.Single(parameter.GetCustomAttributes(typeof(RequiredAttribute), false));
        Assert.False(((RequiredAttribute)required).IsValid(null));
        Assert.True(((RequiredAttribute)required).IsValid(0m));
    }

    [Fact]
    public async Task Service_RejectsMissingFinalWithoutCreatingHistory()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var auditCount = await database.Context.AuditEvents.CountAsync();

        var result = await service.ReplaceMeterDeviceAsync(Request(fixtures.Garage.Id, null), null, default);

        Assert.Equal("meter_device_final_required", result.ErrorCode);
        Assert.Empty(await database.Context.MeterDevices.ToArrayAsync());
        Assert.Empty(await database.Context.MeterReadings.ToArrayAsync());
        Assert.Equal(auditCount, await database.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Service_ValidatesPreviousAccountingMonthEvenWhenReadingDateIsLater()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        database.Context.MeterDevices.Add(new MeterDevice
        {
            GarageId = fixtures.Garage.Id,
            MeterKind = MeterKinds.Water,
            SerialNumber = "OLD-FINAL",
            InstalledOn = new(2026, 5, 1),
            InitialValue = 0m
        });
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var created = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, MeterKinds.Water,
            new(2026, 6, 1), new(2026, 6, 20), 160m, null), null, default);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var reading = await database.Context.MeterReadings.SingleAsync();
        reading.ReadingDate = new(2026, 7, 20);
        await database.Context.SaveChangesAsync();
        var auditCount = await database.Context.AuditEvents.CountAsync();

        var result = await service.ReplaceMeterDeviceAsync(Request(fixtures.Garage.Id, 150m), null, default);

        Assert.Equal("meter_device_final_below_last_reading", result.ErrorCode);
        Assert.DoesNotContain(await database.Context.MeterDevices.ToArrayAsync(), device => device.SerialNumber == "NEW-FINAL");
        Assert.Equal(160m, (await database.Context.MeterReadings.SingleAsync()).CurrentValue);
        Assert.Equal(auditCount, await database.Context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Service_AllowsNewDeviceResetToZeroWithExplicitOldFinal()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var result = await service.ReplaceMeterDeviceAsync(Request(fixtures.Garage.Id, 160m), null, default);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0m, result.Value!.Device.InitialValue);
        Assert.Contains(await database.Context.MeterDevices.ToArrayAsync(), device => device.FinalValue == 160m);
    }

    private static ReplaceMeterDeviceRequest Request(Guid garageId, decimal? final) =>
        new(garageId, MeterKinds.Water, new(2026, 7, 1), new(2026, 7, 10), "NEW-FINAL", 0m, 5m, final, "Замена");
}
