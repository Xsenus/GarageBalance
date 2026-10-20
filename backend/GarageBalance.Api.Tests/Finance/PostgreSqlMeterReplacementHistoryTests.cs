using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMeterReplacementHistoryTests
{
    [PostgreSqlFact]
    public Task CancellationSaveErrorRollsBackTheEntireReplacement() => AssertCancellationRollbackAsync("failure");

    [PostgreSqlFact]
    public Task CancellationTokenRollsBackTheEntireReplacement() => AssertCancellationRollbackAsync("cancellation");

    [PostgreSqlFact]
    public Task CancellationConcurrentReadingChangeRollsBackTheEntireReplacement() => AssertCancellationRollbackAsync("concurrency");

    private static async Task AssertCancellationRollbackAsync(string mode)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var failure = new CancellationSaveInterceptor();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(failure).Options;
        await using var context = new GarageBalanceDbContext(options);
        var garage = new Garage { Number = "PG-ROLLBACK", InitialWaterMeterValue = 10 };
        context.Add(garage);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var replacement = await service.ReplaceMeterDeviceAsync(new(garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "PG-NEW", 0, 2, 15, "Замена"), null, default);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        var value = replacement.Value!;
        var auditCount = await context.AuditEvents.CountAsync();
        var externalVersion = Guid.NewGuid();
        failure.BeforeSave = async () =>
        {
            if (mode == "cancellation") throw new OperationCanceledException("Отмена сохранения.");
            if (mode == "failure") throw new InvalidOperationException("Ошибка сохранения.");
            await using var concurrent = database.CreateContext();
            Assert.Equal(1, await concurrent.MeterReadings.Where(item => item.Id == value.Reading.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Version, externalVersion)));
        };
        var action = () => service.CancelMeterDeviceReplacementAsync(value.Device.Id,
            new(value.Device.Version, value.Reading.Version, new(2026, 6, 1), "Отмена"), null, default);
        if (mode == "concurrency") Assert.Equal("meter_device_conflict", (await action()).ErrorCode);
        else if (mode == "cancellation") await Assert.ThrowsAsync<OperationCanceledException>(action);
        else await Assert.ThrowsAsync<InvalidOperationException>(action);
        failure.BeforeSave = null;
        context.ChangeTracker.Clear();
        var persisted = await context.MeterReadings.SingleAsync();
        Assert.False(persisted.IsCanceled);
        Assert.Equal(value.Reading.Consumption, persisted.Consumption);
        Assert.Equal(mode == "concurrency" ? externalVersion : value.Reading.Version, persisted.Version);
        Assert.False((await context.MeterDevices.SingleAsync(item => item.Id == value.Device.Id)).IsCanceled);
        Assert.Single(await context.MeterDevices.Where(item => item.RemovedOn == null && !item.IsCanceled).ToArrayAsync());
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
    }

    private sealed class CancellationSaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeSave { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null) await BeforeSave();
            return result;
        }
    }

    [PostgreSqlFact]
    public async Task MultipleReplacementsPreserveStartRecalculateAccrualAndCancelPaidMonthKeepingPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PG-REPLACE-CHAIN", PeopleCount = 1, FloorCount = 1, InitialWaterMeterValue = 10m, CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var income = await context.IncomeTypes.SingleAsync(item => item.Code == "water");
        var setting = await context.ChargeServiceSettings.Include(item => item.Tariff).SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsArchived);
        setting.IsRegular = true;
        setting.IsMetered = true;
        setting.PeriodicityMonths = 1;
        setting.AccrualStartMonth = 1;
        setting.Tariff!.CalculationBase = TariffCalculationBases.MeterWater;
        setting.Tariff.Rate = 50m;
        setting.Tariff.EffectiveFrom = new(2026, 1, 1);
        context.Add(garage);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context);
        var before = await finance.CreateMeterReadingAsync(new(garage.Id, "water", new(2026, 6, 1), new(2026, 6, 5), 15, "До замены"), null, default);
        Assert.True(before.Succeeded, before.ErrorMessage);
        var first = await finance.ReplaceMeterDeviceAsync(new(garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "PG-R1", 5, 6, 17, "Первая", before.Value!.Id, before.Value.Version), null, default);
        Assert.True(first.Succeeded, first.ErrorMessage);
        var second = await finance.ReplaceMeterDeviceAsync(new(garage.Id, "water", new(2026, 6, 1), new(2026, 6, 20), "PG-R2", 20, 23, 8, "Вторая", first.Value!.Reading.Id, first.Value.Reading.Version), null, default);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(13m, second.Value!.Reading.Consumption);
        Assert.Equal(650m, (await context.Accruals.SingleAsync()).Amount);
        var dictionary = DictionaryServiceTestFactory.Create(context);
        var changed = await dictionary.UpdateGarageAsync(garage.Id,
            new UpsertGarageRequest(garage.Number, 1, 1, null, 0, 12m, null, null, MeterStartValues: [new("water", 12m)]), null, default);
        Assert.True(changed.Succeeded, changed.ErrorMessage);
        Assert.Equal(550m, (await context.Accruals.SingleAsync()).Amount);
        var history = await finance.GetMeterDeviceHistoryAsync(garage.Id, "water", 0, 25, default);
        var latest = history.Items[0];
        Assert.Equal(12m, (await dictionary.GetGarageMeterStartValuesAsync(garage.Id, default)).Value!.Single(item => item.MeterKind == "water").Value);
        var payment = await finance.CreateIncomeAsync(new(garage.Id, income.Id, new(2026, 6, 25), new(2026, 6, 1), 400, "PG-PAY-REPLACE", null), null, default);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        await AssertPaymentAllocatedAsync(context, payment.Value!.Id, 400m);
        // Cancelling replacements of a paid month recalculates the accrual; the payment is kept and
        // whatever exceeds the reduced accrual stays unallocated as an advance.
        var request = new CancelMeterDeviceReplacementRequest(latest.Version, latest.ReplacementReadingVersion!.Value, new(2026, 6, 1), "Ошибочная замена");
        var canceledSecond = await finance.CancelMeterDeviceReplacementAsync(latest.Id, request, null, default);
        Assert.True(canceledSecond.Succeeded, canceledSecond.ErrorMessage);
        Assert.Equal(6m, canceledSecond.Value!.Reading.Consumption);
        Assert.Equal(300m, (await context.Accruals.AsNoTracking().SingleAsync()).Amount);
        await AssertPaymentAllocatedAsync(context, payment.Value.Id, 300m);
        var canceledFirst = await finance.CancelMeterDeviceReplacementAsync(first.Value.Device.Id,
            new(canceledSecond.Value.RestoredDevice.Version, canceledSecond.Value.Reading.Version, new(2026, 6, 1), "Отмена первой"), null, default);
        Assert.True(canceledFirst.Succeeded, canceledFirst.ErrorMessage);
        Assert.Equal(3m, canceledFirst.Value!.Reading.Consumption);
        Assert.Equal(150m, (await context.Accruals.AsNoTracking().SingleAsync()).Amount);
        await AssertPaymentAllocatedAsync(context, payment.Value.Id, 150m);
        Assert.Equal(2, await context.MeterDevices.CountAsync(item => item.IsCanceled));
        Assert.Single(await finance.GetMeterDevicesAsync(garage.Id, "water", default), device => device.RemovedOn is null);
        var reused = await finance.ReplaceMeterDeviceAsync(new(garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "PG-R1", 5, 6, 17, "Правильная повторная регистрация", canceledFirst.Value.Reading.Id, canceledFirst.Value.Reading.Version), null, default);
        Assert.True(reused.Succeeded, reused.ErrorMessage);
        Assert.Equal(300m, (await context.Accruals.AsNoTracking().SingleAsync()).Amount);
        await AssertPaymentAllocatedAsync(context, payment.Value.Id, 300m);
        Assert.Equal(4, (await finance.GetMeterDeviceHistoryAsync(garage.Id, null, 0, 25, default)).TotalCount);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static async Task AssertPaymentAllocatedAsync(GarageBalanceDbContext context, Guid paymentId, decimal allocated)
    {
        var payment = await context.FinancialOperations.AsNoTracking().SingleAsync(item => item.Id == paymentId);
        Assert.Equal((400m, false), (payment.Amount, payment.IsCanceled));
        var accrual = await context.Accruals.AsNoTracking().SingleAsync();
        var allocations = await context.AccrualPaymentAllocations.AsNoTracking()
            .Where(item => item.FinancialOperationId == paymentId && item.IsActive).ToListAsync();
        Assert.Equal(allocated, allocations.Sum(item => item.Amount));
        Assert.All(allocations, item => Assert.Equal(accrual.Id, item.AccrualId));
        Assert.True(allocated <= accrual.Amount);
    }

    [PostgreSqlFact]
    public async Task MigrationBackfillsLegacyLinksWithoutInventingSnapshotAndCanDowngradeCanceledRecords()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage { Number = "PG-LEGACY-REPLACEMENT", InitialWaterMeterValue = 10m };
        var old = new MeterDevice { Garage = garage, MeterKind = "water", SerialNumber = "OLD", InstalledOn = new(2026, 5, 1), RemovedOn = new(2026, 6, 9), InitialValue = 10, FinalValue = 15 };
        var current = new MeterDevice { Garage = garage, MeterKind = "water", SerialNumber = "NEW", InstalledOn = new(2026, 6, 10), InitialValue = 0 };
        var reading = new MeterReading { Garage = garage, MeterKind = "water", MeterDevice = current, AccountingMonth = new(2026, 6, 1), ReadingDate = new(2026, 6, 10), CurrentValue = 2, PreviousDeviceConsumption = 5, Consumption = 7, IsMeterReplacement = true };
        context.AddRange(garage, old, current, reading);
        await context.SaveChangesAsync();
        await context.GetService<IMigrator>().MigrateAsync("20261005085727_RestoreLegacyAdministratorOpeningDataPermission");
        context.ChangeTracker.Clear();
        await context.GetService<IMigrator>().MigrateAsync();
        var finance = FinanceServiceTestFactory.Create(context);
        var history = await finance.GetMeterDeviceHistoryAsync(garage.Id, null, 0, 25, default);
        var device = history.Items[0];
        Assert.Equal(old.Id, device.ReplacedDeviceId);
        Assert.Equal(reading.Id, device.ReplacementReadingId);
        Assert.True(device.RequiresRestoredValue);
        var request = new CancelMeterDeviceReplacementRequest(device.Version, device.ReplacementReadingVersion!.Value, new(2026, 6, 1), "Отмена старой записи");
        Assert.Equal("meter_device_restored_value_required", (await finance.CancelMeterDeviceReplacementAsync(current.Id, request, null, default)).ErrorCode);
        var canceled = await finance.CancelMeterDeviceReplacementAsync(current.Id, request with { RestoredCurrentValue = 14.125m }, null, default);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.Equal(4.125m, canceled.Value!.Reading.Consumption);
        Assert.Equal(14.125m, canceled.Value.Reading.CurrentValue);
        await context.GetService<IMigrator>().MigrateAsync("20261005085727_RestoreLegacyAdministratorOpeningDataPermission");
        await context.GetService<IMigrator>().MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.Single(await finance.GetMeterDevicesAsync(garage.Id, "water", default), device => device.RemovedOn is null);
        Assert.Equal(4.125m, (await context.MeterReadings.SingleAsync()).Consumption);
    }
}
