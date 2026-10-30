using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceServiceTests
{
    [Fact]
    public async Task ReplacementEdit_RecalculatesChainPreservesDeviceIdentityAndRecordsAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.IncomeType.Code = "water";
        var tariff = new Tariff { Name = "EDIT-WATER", CalculationBase = TariffCalculationBases.MeterWater, Rate = 50m, EffectiveFrom = new(2026, 1, 1) };
        database.Context.Tariffs.Add(tariff);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replacement = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "EDIT-NEW", 0, 2, 15, "Замена"), null, default);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        var next = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, "water", new(2026, 7, 1), new(2026, 7, 20), 5, null), null, default);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.True((await service.GenerateRegularAccrualsAsync(new(fixtures.IncomeType.Id, tariff.Id, new(2026, 6, 1), null), null, default)).Succeeded);
        var payment = await service.CreateIncomeAsync(new(fixtures.Garage.Id, fixtures.IncomeType.Id, new(2026, 6, 25), new(2026, 6, 1), 100, "EDIT-PAYMENT", "Оплата"), null, default);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        var paidBefore = await database.Context.FinancialOperations.AsNoTracking().SingleAsync();
        var details = await service.GetMeterDeviceReplacementAsync(replacement.Value!.Device.Id, default);
        Assert.True(details.Succeeded, details.ErrorMessage);
        var before = details.Value!;
        var request = new UpdateMeterDeviceReplacementRequest(before.Device.Version, before.RemovedDevice.Version, before.Reading!.Version,
            new(2026, 6, 1), new(2026, 6, 11), "EDIT-CORRECTED", 19, 1, "Уточнён акт замены");
        var edited = await service.UpdateMeterDeviceReplacementAsync(before.Device.Id, request, null, default);
        Assert.True(edited.Succeeded, edited.ErrorMessage);
        Assert.Equal(before.Device.Id, edited.Value!.Device.Id);
        Assert.Equal(before.RemovedDevice.Id, edited.Value.RemovedDevice.Id);
        Assert.Equal(19m, edited.Value.RemovedDevice.FinalValue);
        Assert.Equal(1m, edited.Value.Device.InitialValue);
        Assert.Equal(2m, edited.Value.Reading!.CurrentValue);
        Assert.Equal(19m - before.RemovedDevice.InitialValue + 1m, edited.Value.Reading.Consumption);
        Assert.Equal(3m, (await database.Context.MeterReadings.SingleAsync(item => item.Id == next.Value!.Id)).Consumption);
        Assert.Equal(2, await database.Context.MeterDevices.CountAsync());
        var paidAfter = await database.Context.FinancialOperations.AsNoTracking().SingleAsync();
        Assert.Equal((paidBefore.Id, paidBefore.Version, paidBefore.Amount, paidBefore.OperationDate, paidBefore.AccountingMonth, paidBefore.IsCanceled),
            (paidAfter.Id, paidAfter.Version, paidAfter.Amount, paidAfter.OperationDate, paidAfter.AccountingMonth, paidAfter.IsCanceled));
        Assert.Equal(edited.Value.Reading.Consumption * 50m, (await database.Context.Accruals.SingleAsync()).Amount);
        Assert.Contains(await database.Context.AuditEvents.ToArrayAsync(), item => item.Action == "finance.meter_device_replacement_updated");
        Assert.NotEqual(before.Device.Version, edited.Value.Device.Version);
    }

    [Theory]
    [InlineData("missing-final", "meter_device_final_required")]
    [InlineData("missing-initial", "meter_device_final_required")]
    [InlineData("device-version", "meter_device_conflict")]
    [InlineData("old-version", "meter_device_conflict")]
    [InlineData("reading-version", "meter_device_conflict")]
    [InlineData("other-month", "meter_device_replacement_date_month_mismatch")]
    [InlineData("below-old", "meter_device_final_below_last_reading")]
    [InlineData("above-new", "meter_device_current_below_initial")]
    [InlineData("duplicate", "meter_device_serial_duplicate")]
    [InlineData("empty-serial", "meter_device_values_invalid")]
    [InlineData("empty-reason", "meter_device_values_invalid")]
    [InlineData("negative-initial", "meter_device_values_invalid")]
    [InlineData("negative-final", "meter_device_values_invalid")]
    public async Task ReplacementEdit_RejectsInvalidDraftWithoutChangingHistory(string invalid, string code)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replacement = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "EDIT-NEW", 0, 2, 15, "Замена"), null, default);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        var before = (await service.GetMeterDeviceReplacementAsync(replacement.Value!.Device.Id, default)).Value!;
        var request = new UpdateMeterDeviceReplacementRequest(before.Device.Version, before.RemovedDevice.Version, before.Reading!.Version,
            new(2026, 6, 1), new(2026, 6, 11), "EDIT-CORRECTED", 19, 1, "Исправление");
        request = invalid switch
        {
            "missing-final" => request with { RemovedDeviceFinalValue = null },
            "missing-initial" => request with { NewInitialValue = null },
            "device-version" => request with { ExpectedDeviceVersion = Guid.NewGuid() },
            "old-version" => request with { ExpectedRemovedDeviceVersion = Guid.NewGuid() },
            "reading-version" => request with { ExpectedReadingVersion = Guid.NewGuid() },
            "other-month" => request with { ReplacementDate = new(2026, 7, 1) },
            "below-old" => request with { RemovedDeviceFinalValue = 0 },
            "above-new" => request with { NewInitialValue = 3 },
            "duplicate" => request with { NewSerialNumber = before.RemovedDevice.SerialNumber },
            "empty-serial" => request with { NewSerialNumber = " " },
            "empty-reason" => request with { Reason = " " },
            "negative-initial" => request with { NewInitialValue = -1 },
            "negative-final" => request with { RemovedDeviceFinalValue = -1 },
            _ => request
        };
        var auditCount = await database.Context.AuditEvents.CountAsync();
        var result = await service.UpdateMeterDeviceReplacementAsync(before.Device.Id, request, null, default);
        Assert.Equal(code, result.ErrorCode);
        var after = (await service.GetMeterDeviceReplacementAsync(before.Device.Id, default)).Value!;
        Assert.Equal(before.Device, after.Device);
        Assert.Equal(before.RemovedDevice, after.RemovedDevice);
        Assert.Equal(before.Reading, after.Reading);
        Assert.Equal(auditCount, await database.Context.AuditEvents.CountAsync());
    }
}
