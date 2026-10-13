using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceServiceTests
{
    [Fact]
    public async Task ReplacementHistory_CustomServiceKeepsItsScopeBaselineAndName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var setting = new ChargeServiceSetting
        {
            Name = "Коммерческий прибор",
            IsRegular = true,
            IsMetered = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            IncomeType = fixtures.IncomeType,
            UnitName = "ед.",
            AppliesToSelectedGarages = true,
            GarageIds = [fixtures.Garage.Id]
        };
        setting.MeterKind = MeterKinds.ForService(setting.Id);
        var excluded = new GarageBalance.Api.Domain.Dictionaries.Garage { Number = "EXCLUDED" };
        database.Context.AddRange(setting, excluded);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context, businessDate: new(2026, 6, 1));
        var rejected = await service.ReplaceMeterDeviceAsync(new(excluded.Id, setting.MeterKind,
            new(2026, 6, 1), new(2026, 6, 10), "OUT", 0, 2, 5, "Замена"), null, default);
        Assert.Equal("meter_start_service_not_applicable", rejected.ErrorCode);
        Assert.Empty(await database.Context.MeterDevices.ToArrayAsync());
        var baseline = await service.ApplyStartValuesAsync(fixtures.Garage,
            [new(setting.MeterKind, 10, new DateOnly(2026, 6, 1))], null, default);
        Assert.True(baseline.Succeeded, baseline.ErrorMessage);
        await database.Context.SaveChangesAsync(); // The garage-card caller commits baseline changes.
        var before = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, setting.MeterKind,
            new(2026, 6, 1), new(2026, 6, 8), 14, "До замены"), null, default);
        Assert.True(before.Succeeded, before.ErrorMessage);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, setting.MeterKind,
            new(2026, 6, 1), new(2026, 6, 10), "CUSTOM", 20, 22, 15, "Замена",
            before.Value!.Id, before.Value.Version), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        Assert.Equal(7m, replaced.Value!.Reading.Consumption);
        var page = await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, setting.MeterKind, 0, 25, default);
        Assert.All(page.Items, item => Assert.Equal(setting.Name, item.ServiceName));
        var canceled = await service.CancelMeterDeviceReplacementAsync(replaced.Value.Device.Id,
            new(replaced.Value.Device.Version, replaced.Value.Reading.Version, new(2026, 6, 1), "Отмена"), null, default);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.Equal(4m, canceled.Value!.Reading!.Consumption);
        Assert.Equal(10m, (await service.GetStartValuesAsync(fixtures.Garage, default))
            .Single(item => item.MeterKind == setting.MeterKind).Value);
    }

    [Fact]
    public async Task ReplacementHistory_RespectsDisabledCommentRequirementAndClampsHistoryPage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "OPTIONAL", 0, 2, 15, "Замена"), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        using var scope = ActionCommentRequirementContext.Push(false);
        Assert.True((await service.CancelMeterDeviceReplacementAsync(replaced.Value!.Device.Id,
            new(replaced.Value.Device.Version, replaced.Value.Reading.Version, new(2026, 6, 1), ""), null, default)).Succeeded);
        var page = await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, "water", -2, 1000, default);
        Assert.Equal(0, page.Offset);
        Assert.Equal(100, page.Limit);
        Assert.Equal(2, page.TotalCount);
        Assert.Empty((await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, "invalid", 0, 0, default)).Items);
        Assert.Empty((await service.GetMeterDeviceHistoryAsync(Guid.NewGuid(), null, 0, 25, default)).Items);
    }

    [Theory]
    [InlineData("broken-json", "meter_device_snapshot_invalid")]
    [InlineData("null-json", "meter_device_snapshot_invalid")]
    [InlineData("ordinary", "meter_device_not_replacement")]
    [InlineData("long-reason", "meter_device_cancel_reason_required")]
    [InlineData("later-date", "meter_device_later_readings")]
    public async Task ReplacementHistory_RejectsDamagedSnapshotAndInvalidHistoricalState(string state, string code)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "DAMAGED", 0, 2, 15, "Замена"), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        var device = await database.Context.MeterDevices.SingleAsync(item => item.Id == replaced.Value!.Device.Id);
        if (state == "broken-json") device.PreviousReadingSnapshotJson = "broken";
        if (state == "null-json") device.PreviousReadingSnapshotJson = "null";
        if (state == "ordinary") device.ReplacedDeviceId = null;
        if (state == "later-date") (await database.Context.MeterReadings.SingleAsync()).ReadingDate = new(2026, 6, 11);
        await database.Context.SaveChangesAsync();
        var result = await service.CancelMeterDeviceReplacementAsync(device.Id,
            new(device.Version, replaced.Value!.Reading.Version, new(2026, 6, 1), state == "long-reason" ? new string('x', 501) : "Отмена"), null, default);
        Assert.Equal(code, result.ErrorCode);
        Assert.False((await database.Context.MeterDevices.SingleAsync(item => item.Id == device.Id)).IsCanceled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ReplacementHistory_SaveFailureRollsBackDeviceReadingAndAudit(bool outerTransaction, bool cancellation)
    {
        var failure = new ReplacementSaveFailureInterceptor();
        await using var database = await TestDatabase.CreateAsync(failure);
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "ROLLBACK", 0, 2, 15, "Замена"), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        var device = replaced.Value!.Device;
        var reading = replaced.Value.Reading;
        var auditCount = await database.Context.AuditEvents.CountAsync();
        await using var transaction = outerTransaction ? await database.Context.Database.BeginTransactionAsync() : null;
        failure.Enabled = true;
        failure.Cancellation = cancellation;
        var action = () => service.CancelMeterDeviceReplacementAsync(device.Id, new(device.Version, reading.Version, new(2026, 6, 1), "Отмена"), null, default);
        if (cancellation) await Assert.ThrowsAsync<OperationCanceledException>(action);
        else await Assert.ThrowsAsync<InvalidOperationException>(action);
        failure.Enabled = false;
        database.Context.ChangeTracker.Clear();
        Assert.False((await database.Context.MeterDevices.SingleAsync(item => item.Id == device.Id)).IsCanceled);
        Assert.Single(await database.Context.MeterDevices.Where(item => item.RemovedOn == null && !item.IsCanceled).ToArrayAsync());
        var persisted = await database.Context.MeterReadings.SingleAsync();
        Assert.False(persisted.IsCanceled);
        Assert.Equal(reading.Version, persisted.Version);
        Assert.Equal(reading.Consumption, persisted.Consumption);
        Assert.Equal(auditCount, await database.Context.AuditEvents.CountAsync());
        if (transaction is not null) await transaction.CommitAsync(); // The caller's transaction remains usable.
    }

    private sealed class ReplacementSaveFailureInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public bool Cancellation { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                if (Cancellation) throw new OperationCanceledException("Имитирована отмена сохранения.");
                throw new InvalidOperationException("Имитирована ошибка сохранения после освобождения активного прибора.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Theory]
    [InlineData("water", 10)]
    [InlineData("electricity", 100)]
    public async Task ReplacementHistory_ThreeReplacementsKeepEverySegmentAndCancelInReverse(string kind, int start)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var original = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, kind, new(2026, 5, 1), new(2026, 5, 20), start + 5, "База"), null, default);
        Assert.True(original.Succeeded, original.ErrorMessage);
        var before = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, kind, new(2026, 6, 1), new(2026, 6, 8), start + 9, "До замены"), null, default);
        var first = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, kind, new(2026, 6, 1), new(2026, 6, 10), "R1", 5, 7, start + 10, "Первая", before.Value!.Id, before.Value.Version), null, default);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(7m, first.Value!.Reading.Consumption);
        var second = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, kind, new(2026, 6, 1), new(2026, 6, 20), "R2", 20, 23, 9, "Вторая", first.Value.Reading.Id, first.Value.Reading.Version), null, default);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(9m, second.Value!.Reading.PreviousDeviceConsumption);
        Assert.Equal(12m, second.Value.Reading.Consumption);
        var third = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, kind, new(2026, 6, 1), new(2026, 6, 25), "R3", 100, 101, 25, "Третья", second.Value.Reading.Id, second.Value.Reading.Version), null, default);
        Assert.True(third.Succeeded, third.ErrorMessage);
        Assert.Equal(14m, third.Value!.Reading.PreviousDeviceConsumption);
        Assert.Equal(15m, third.Value.Reading.Consumption);
        var history = await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, kind, 0, 2, default);
        Assert.Equal(4, history.TotalCount);
        Assert.Equal("R3", history.Items[0].SerialNumber);
        Assert.NotNull(history.Items[0].ReplacementReadingVersion);
        Assert.Equal(2, history.Items.Count);
        var early = await service.CancelMeterDeviceReplacementAsync(first.Value.Device.Id, new(first.Value.Device.Version, third.Value.Reading.Version, new(2026, 6, 1), "Не по порядку"), null, default);
        Assert.Equal("meter_device_conflict", early.ErrorCode); // Removal changed the device version.
        var removed = (await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, kind, 0, 100, default)).Items.Single(item => item.Id == first.Value.Device.Id);
        Assert.Equal("meter_device_later_replacement", (await service.CancelMeterDeviceReplacementAsync(removed.Id,
            new(removed.Version, third.Value.Reading.Version, new(2026, 6, 1), "Ранняя отмена"), null, default)).ErrorCode);
        var thirdCanceled = await service.CancelMeterDeviceReplacementAsync(third.Value.Device.Id, new(third.Value.Device.Version, third.Value.Reading.Version, new(2026, 6, 1), "Отмена третьей"), null, default);
        Assert.True(thirdCanceled.Succeeded, thirdCanceled.ErrorMessage);
        Assert.Equal(12m, thirdCanceled.Value!.Reading.Consumption);
        Assert.Equal(23m, thirdCanceled.Value.Reading.CurrentValue);
        Assert.True(thirdCanceled.Value.Reading.IsMeterReplacement);
        var secondCanceled = await service.CancelMeterDeviceReplacementAsync(second.Value.Device.Id, new(thirdCanceled.Value.RestoredDevice.Version, thirdCanceled.Value.Reading.Version, new(2026, 6, 1), "Отмена второй"), null, default);
        Assert.True(secondCanceled.Succeeded, secondCanceled.ErrorMessage);
        Assert.Equal(7m, secondCanceled.Value!.Reading.Consumption);
        var firstCanceled = await service.CancelMeterDeviceReplacementAsync(first.Value.Device.Id, new(secondCanceled.Value.RestoredDevice.Version, secondCanceled.Value.Reading.Version, new(2026, 6, 1), "Отмена первой"), null, default);
        Assert.True(firstCanceled.Succeeded, firstCanceled.ErrorMessage);
        Assert.Equal(4m, firstCanceled.Value!.Reading.Consumption);
        Assert.Equal("До замены", firstCanceled.Value.Reading.Comment);
        Assert.False(firstCanceled.Value.Reading.IsMeterReplacement);
        Assert.Equal(3, await database.Context.MeterDevices.CountAsync(device => device.IsCanceled));
        Assert.Single(await service.GetMeterDevicesAsync(fixtures.Garage.Id, kind, default));
        Assert.Equal(4, (await service.GetMeterDeviceHistoryAsync(fixtures.Garage.Id, null, 0, 100, default)).TotalCount);
        Assert.Equal("meter_device_already_canceled", (await service.CancelMeterDeviceReplacementAsync(first.Value.Device.Id,
            new(first.Value.Device.Version, firstCanceled.Value.Reading.Version, new(2026, 6, 1), "Повтор"), null, default)).ErrorCode);
        Assert.Equal(3, await database.Context.AuditEvents.CountAsync(item => item.Action == "finance.meter_device_replacement_canceled"));
    }

    [Fact]
    public async Task ReplacementHistory_CancelWithoutPriorMonthlyReadingAndRejectLaterReadings()
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "FIRST", 20, 22, 15, "Первая замена"), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        Assert.Equal(7m, replaced.Value!.Reading.Consumption);
        var later = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, "water", new(2026, 7, 1), new(2026, 7, 20), 25, null), null, default);
        Assert.True(later.Succeeded, later.ErrorMessage);
        var request = new CancelMeterDeviceReplacementRequest(replaced.Value.Device.Version, replaced.Value.Reading.Version, new(2026, 6, 1), "Отмена");
        Assert.Equal("meter_device_later_readings", (await service.CancelMeterDeviceReplacementAsync(replaced.Value.Device.Id, request, null, default)).ErrorCode);
        Assert.True((await service.CancelMeterReadingAsync(later.Value!.Id, new("Отмена последующего"), null, default)).Succeeded);
        var canceled = await service.CancelMeterDeviceReplacementAsync(replaced.Value.Device.Id, request, null, default);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.True(canceled.Value!.Reading.IsCanceled);
        Assert.Equal(10m, canceled.Value.RestoredDevice.InitialValue);
        Assert.Equal("meter_device_replacement_canceled", (await service.RestoreMeterReadingAsync(replaced.Value.Reading.Id, null, default)).ErrorCode);
    }

    [Theory]
    [InlineData("reason", "meter_device_cancel_reason_required")]
    [InlineData("missing", "meter_device_not_found")]
    [InlineData("device-version", "meter_device_conflict")]
    [InlineData("reading-version", "meter_reading_conflict")]
    [InlineData("month", "meter_device_replacement_month_mismatch")]
    [InlineData("date", "meter_device_replacement_date_invalid")]
    public async Task ReplacementHistory_RejectsInvalidRequestsWithoutMutation(string invalid, string code)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);
        var replaced = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "CHECK", 0, 2, 15, "Замена"), null, default);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        FinanceResult<MeterDeviceReplacementCancellationDto>? canceled = null;
        if (invalid == "date")
        {
            var repeated = await service.ReplaceMeterDeviceAsync(new(fixtures.Garage.Id, "water", new(2026, 6, 1), new(2026, 6, 10), "SAME-DATE", 0, 0, 2, "Повтор", replaced.Value!.Reading.Id, replaced.Value.Reading.Version), null, default);
            Assert.Equal(code, repeated.ErrorCode);
        }
        else
        {
            canceled = await service.CancelMeterDeviceReplacementAsync(invalid == "missing" ? Guid.NewGuid() : replaced.Value!.Device.Id,
                new(invalid == "device-version" ? Guid.NewGuid() : replaced.Value!.Device.Version,
                    invalid == "reading-version" ? Guid.NewGuid() : replaced.Value!.Reading.Version,
                    new(2026, invalid == "month" ? 7 : 6, 1), invalid == "reason" ? " " : "Отмена"), null, default);
            Assert.Equal(code, canceled!.ErrorCode);
        }
        Assert.DoesNotContain(database.Context.MeterDevices, device => device.IsCanceled);
        Assert.Equal(7m, database.Context.MeterReadings.Single().Consumption);
    }
}
