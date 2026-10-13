using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterAccountingStartWorkflowTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    [Theory]
    [InlineData(MeterKinds.Water, 0)]
    [InlineData(MeterKinds.Electricity, 100)]
    public async Task CorrectedStart_AllowsSeptemberAndFollowingMonthWithoutChangingBase(string kind, int baseline)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCorrectionAsync(database.Context, kind, baseline);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CorrectedStartAllowsSeptemberAndReturnsMonthInYearPage()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCorrectionAsync(context, MeterKinds.Electricity, 100);
    }

    private static async Task AssertCorrectionAsync(GarageBalanceDbContext context, string kind, decimal baseline)
    {
        var garage = await SeedAsync(context, kind, baseline);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October.AddDays(12));
        var before = await finance.CreateMeterReadingAsync(new(garage.Id, kind, September, September.AddDays(20), baseline + 10, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_before_baseline", before.ErrorCode);
        Assert.Equal("Показание должно относиться к месяцу после начального показания из карточки гаража.", before.ErrorMessage);
        await CorrectAsync(context, garage);
        Assert.Equal(September.AddMonths(-1), garage.InitialMeterReadingMonth);
        var first = await finance.CreateMeterReadingAsync(new(garage.Id, kind, September, September.AddDays(20), baseline + 10, null), null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(baseline, first.Value!.PreviousValue);
        Assert.Equal(10m, first.Value.Consumption);
        var next = await finance.CreateMeterReadingAsync(new(garage.Id, kind, October, October.AddDays(20), baseline + 20, null), null, CancellationToken.None);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.Equal(baseline + 10, next.Value!.PreviousValue);
        Assert.Equal(10m, next.Value.Consumption);
        var earlier = await finance.CreateMeterReadingAsync(new(garage.Id, kind, September.AddMonths(-1), September.AddDays(-2), baseline, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_before_accounting_start", earlier.ErrorCode);
        Assert.Contains("09.2026", earlier.ErrorMessage);
        var page = await finance.GetMeterReadingYearPageAsync(new(2026, kind), CancellationToken.None);
        Assert.True(page.Succeeded, page.ErrorMessage);
        var row = Assert.Single(page.Value!.Garages, row => row.Id == garage.Id);
        Assert.Equal(September, row.AccountingStartMonth);
        Assert.Equal(September.AddMonths(-1), row.InitialReadingMonth);
        Assert.Equal(baseline, row.InitialReadingValue);
        Assert.Equal(2, page.Value.Readings.Count(row => row.GarageId == garage.Id));
    }

    [Fact]
    public async Task CorrectedStart_AlignsOnlyGeneratedOriginalDeviceAndAllowsFirstMonthReplacement()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedAsync(context, MeterKinds.Electricity, 100);
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial, InstalledOn = October.AddDays(20), InitialValue = 100 };
        context.MeterDevices.Add(device);
        await context.SaveChangesAsync();
        await CorrectAsync(context, garage);
        Assert.Equal(September.AddDays(-1), device.InstalledOn);
        Assert.Equal(100m, device.InitialValue);
        Assert.Single(await context.AuditEvents.Where(row => row.Action == "finance.legacy_meter_opening_date_adjusted").ToListAsync());
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October);
        var replaced = await finance.ReplaceMeterDeviceAsync(new(garage.Id, MeterKinds.Electricity, September, September.AddDays(14), "synthetic-new", 5, 8, 110, "Акт замены"), null, CancellationToken.None);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        Assert.Equal(13m, replaced.Value!.Reading.Consumption);
        Assert.Equal(September.AddDays(14), replaced.Value.Device.InstalledOn);
        Assert.Equal(September.AddDays(13), device.RemovedOn);
    }

    [Fact]
    public async Task CorrectedStart_DoesNotRewriteNumberedDeviceInstallationOrCreateConflictingDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedAsync(context, MeterKinds.Electricity, 100);
        var installed = October.AddDays(10);
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "synthetic-numbered", InstalledOn = installed, InitialValue = 100 };
        context.MeterDevices.Add(device);
        await context.SaveChangesAsync();
        await CorrectAsync(context, garage);
        Assert.Equal(installed, device.InstalledOn);
        var result = await FinanceServiceTestFactory.Create(context, businessDate: October).CreateMeterReadingAsync(
            new(garage.Id, MeterKinds.Electricity, September, September.AddDays(20), 110, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_outside_device_period", result.ErrorCode);
        Assert.Single(await context.MeterDevices.ToListAsync());
        Assert.Empty(await context.MeterReadings.ToListAsync());
        Assert.Equal(installed, (await context.MeterDevices.SingleAsync()).InstalledOn);
    }

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData(MeterKinds.Electricity)]
    public async Task ExplicitStart_RejectsEarlierReplacementAndPreservesUnknownBaseline(string kind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedAsync(database.Context, kind, null);
        await CorrectAsync(database.Context, garage);
        var finance = FinanceServiceTestFactory.Create(database.Context, businessDate: October);
        var earlier = await finance.ReplaceMeterDeviceAsync(new(garage.Id, kind, September.AddMonths(-1), September.AddDays(-2), "synthetic", 0, 1, 1, "Проверка месяца"), null, CancellationToken.None);
        Assert.Equal("meter_reading_before_accounting_start", earlier.ErrorCode);
        var first = await finance.CreateMeterReadingAsync(new(garage.Id, kind, September, September.AddDays(20), 10, null), null, CancellationToken.None);
        Assert.Equal(kind == MeterKinds.Water ? "water_meter_reading_baseline_required" : "meter_connection_required", first.ErrorCode);
        Assert.Empty(await database.Context.MeterReadings.ToListAsync());
        Assert.Empty(await database.Context.MeterDevices.ToListAsync());
    }

    [Fact]
    public async Task Correction_FailedMeterEditAndCanceledCompletionRollBackMonthAndDeviceBoundary()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedAsync(context, MeterKinds.Electricity, 100);
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial, InstalledOn = October.AddDays(20), InitialValue = 100 };
        context.MeterDevices.Add(device);
        await context.SaveChangesAsync();
        var request = Request(garage) with { MeterStartValues = [new(MeterKinds.ForService(Guid.NewGuid()), 0, September)] };
        var failed = await DictionaryServiceTestFactory.Create(context, October).UpdateGarageAsync(garage.Id, request, null, CancellationToken.None);
        Assert.Equal("meter_start_service_not_applicable", failed.ErrorCode);
        garage = await context.Garages.SingleAsync();
        Assert.Null(garage.AccountingStartMonth);
        Assert.Equal(September, garage.InitialMeterReadingMonth);
        Assert.Equal(October.AddDays(20), (await context.MeterDevices.SingleAsync()).InstalledOn);
        var change = await FinanceServiceTestFactory.Create(context, businessDate: October).BeginAsync(garage, September, garage.Version, false, null, CancellationToken.None);
        Assert.True(change.Succeeded, change.ErrorMessage);
        await using (change.Value!)
        {
            await context.SaveChangesAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change.Value!.CompleteAsync(cancellation.Token));
        }
        Assert.Null((await context.Garages.SingleAsync()).AccountingStartMonth);
        Assert.Equal(October.AddDays(20), (await context.MeterDevices.SingleAsync()).InstalledOn);
        Assert.Empty(await context.AuditEvents.Where(row => row.Action == "finance.legacy_meter_opening_date_adjusted").ToListAsync());
    }

    [Fact]
    public async Task Correction_KeepsIndividualConnectionDateAndItsFirstMonthBaseline()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedAsync(context, MeterKinds.Water, 50);
        var connectedOn = October.AddDays(14);
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Water, SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial, InstalledOn = connectedOn, InitialValue = 50 };
        var connection = new GarageMeterConnection { Garage = garage, MeterKind = MeterKinds.Water, ConnectedOn = connectedOn, BaselineMeterDevice = device };
        context.AddRange(device, connection);
        await context.SaveChangesAsync();
        await CorrectAsync(context, garage);
        Assert.Equal(connectedOn, device.InstalledOn);
        Assert.Equal(connectedOn, connection.ConnectedOn);
        Assert.Equal(50m, device.InitialValue);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October.AddDays(30));
        var september = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, September, September.AddDays(20), 60, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_outside_connection", september.ErrorCode);
        var earlyOctober = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, October, October, 60, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_outside_connection", earlyOctober.ErrorCode);
        var october = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, October, October.AddDays(20), 60, null), null, CancellationToken.None);
        Assert.True(october.Succeeded, october.ErrorMessage);
        Assert.Equal(50m, october.Value!.PreviousValue);
        Assert.Equal(10m, october.Value.Consumption);
        Assert.Empty(await context.AuditEvents.Where(row => row.Action == "finance.legacy_meter_opening_date_adjusted").ToListAsync());
    }

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData(MeterKinds.Electricity)]
    public async Task CorrectedFirstMonth_MultipleReplacementsTransferBaselineCancelAndKeepHistory(string kind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertFirstMonthReplacementChainAsync(database.Context, kind);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CorrectedFirstMonthReplacementChain()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertFirstMonthReplacementChainAsync(context, MeterKinds.Electricity);
    }

    private static async Task AssertFirstMonthReplacementChainAsync(GarageBalanceDbContext context, string kind)
    {
        var garage = await SeedAsync(context, kind, 100);
        var income = await context.IncomeTypes.SingleOrDefaultAsync(row => row.Code == kind);
        if (income is null)
        {
            income = new IncomeType { Name = "Синтетическая счётчиковая услуга", Code = kind };
            context.IncomeTypes.Add(income);
        }
        var setting = await context.ChargeServiceSettings.Include(row => row.Tariff)
            .SingleOrDefaultAsync(row => row.IncomeTypeId == income.Id && !row.IsArchived);
        if (setting is null)
        {
            setting = new ChargeServiceSetting { Name = income.Name, IncomeType = income };
            context.ChargeServiceSettings.Add(setting);
        }
        setting.IsRegular = true;
        setting.IsMetered = true;
        setting.MeterKind = kind;
        setting.PeriodicityMonths = 1;
        setting.AccrualStartMonth = 1;
        if (setting.Tariff is null)
        {
            setting.Tariff = new Tariff
            {
                Name = "Синтетический тариф",
                CalculationBase = kind == MeterKinds.Water
                ? TariffCalculationBases.MeterWater : TariffCalculationBases.MeterElectricity
            };
            context.Tariffs.Add(setting.Tariff);
        }
        setting.HasTieredTariff = false;
        setting.Tariff.Rate = 50;
        setting.Tariff.EffectiveFrom = new(2026, 1, 1);
        await context.SaveChangesAsync();
        await CorrectAsync(context, garage);
        var service = FinanceServiceTestFactory.Create(context, businessDate: October);
        var before = await service.CreateMeterReadingAsync(new(garage.Id, kind, September, September.AddDays(4), 105, "До замен"), null, default);
        Assert.True(before.Succeeded, before.ErrorMessage);
        var first = await service.ReplaceMeterDeviceAsync(new(garage.Id, kind, September, September.AddDays(9), "FIRST", 5, 6, 107,
            "Первая замена", before.Value!.Id, before.Value.Version), null, default);
        Assert.True(first.Succeeded, first.ErrorMessage);
        var second = await service.ReplaceMeterDeviceAsync(new(garage.Id, kind, September, September.AddDays(19), "SECOND", 20, 23, 8,
            "Вторая замена", first.Value!.Reading.Id, first.Value.Reading.Version), null, default);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(13m, second.Value!.Reading.Consumption);
        Assert.Equal(650m, (await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id && row.AccountingMonth == September)).Amount);

        var dictionary = DictionaryServiceTestFactory.Create(context, October);
        var corrected = await dictionary.UpdateGarageAsync(garage.Id, Request(garage) with
        {
            InitialWaterMeterValue = kind == MeterKinds.Water ? 102 : null,
            InitialElectricityMeterValue = kind == MeterKinds.Electricity ? 102 : null,
            MeterStartValues = [new(kind, 102, Reason: "Исправление синтетической базы после замен")]
        }, null, default);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        Assert.Equal(102m, (await dictionary.GetGarageMeterStartValuesAsync(garage.Id, default)).Value!.Single(row => row.MeterKind == kind).Value);
        Assert.Equal(550m, (await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id && row.AccountingMonth == September)).Amount);
        var page = await service.GetMeterReadingYearPageAsync(new(2026, kind), default);
        var yearGarage = Assert.Single(page.Value!.Garages, row => row.Id == garage.Id);
        Assert.Equal(102m, yearGarage.StartValue);

        var history = await service.GetMeterDeviceHistoryAsync(garage.Id, kind, 0, 25, default);
        Assert.Equal(3, history.TotalCount);
        var latest = Assert.Single(history.Items, row => row.Id == second.Value.Device.Id);
        var cancellation = new CancelMeterDeviceReplacementRequest(latest.Version, latest.ReplacementReadingVersion!.Value, September, "Отмена второй");
        var payment = await service.CreateIncomeAsync(new(garage.Id, income.Id, September.AddDays(24), September, 50, "SYNTHETIC-PAID", null), null, default);
        Assert.True(payment.Succeeded, payment.ErrorMessage);
        var denied = await service.CancelMeterDeviceReplacementAsync(latest.Id, cancellation, null, default);
        Assert.Equal("meter_reading_accrual_paid", denied.ErrorCode);
        Assert.False(await context.MeterDevices.AnyAsync(row => row.GarageId == garage.Id && row.IsCanceled));
        Assert.Equal(550m, (await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id && row.AccountingMonth == September)).Amount);
        var undonePayment = await service.CancelOperationAsync(payment.Value!.Id, new("Отмена синтетического платежа"), null, default);
        Assert.True(undonePayment.Succeeded, undonePayment.ErrorMessage);
        var canceledSecond = await service.CancelMeterDeviceReplacementAsync(latest.Id,
            cancellation, null, default);
        Assert.True(canceledSecond.Succeeded, canceledSecond.ErrorMessage);
        Assert.Equal(6m, canceledSecond.Value!.Reading.Consumption);
        Assert.Equal(300m, (await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id && row.AccountingMonth == September)).Amount);
        var canceledFirst = await service.CancelMeterDeviceReplacementAsync(first.Value.Device.Id,
            new(canceledSecond.Value.RestoredDevice.Version, canceledSecond.Value.Reading.Version, September, "Отмена первой"), null, default);
        Assert.True(canceledFirst.Succeeded, canceledFirst.ErrorMessage);
        Assert.Equal(3m, canceledFirst.Value!.Reading.Consumption);
        Assert.Equal(105m, canceledFirst.Value.Reading.CurrentValue);
        Assert.Equal("До замен", canceledFirst.Value.Reading.Comment);
        Assert.False(canceledFirst.Value.Reading.IsMeterReplacement);
        Assert.Equal(150m, (await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id && row.AccountingMonth == September)).Amount);
        var persistedHistory = await service.GetMeterDeviceHistoryAsync(garage.Id, kind, 0, 25, default);
        Assert.Equal(3, persistedHistory.TotalCount);
        Assert.Equal(2, persistedHistory.Items.Count(row => row.IsCanceled));
        Assert.Equal(102m, Assert.Single(await service.GetMeterDevicesAsync(garage.Id, kind, default)).InitialValue);
        Assert.Equal(2, await context.AuditEvents.CountAsync(row => row.Action == "finance.meter_device_replacement_canceled"));
    }

    private static async Task<Garage> SeedAsync(GarageBalanceDbContext context, string kind, decimal? baseline)
    {
        var garage = new Garage
        {
            Number = "synthetic-first-month",
            PeopleCount = 1,
            FloorCount = 1,
            RegisteredOn = October.AddDays(12),
            InitialMeterReadingMonth = September,
            InitialElectricityMeterValue = kind == MeterKinds.Electricity ? baseline : null,
            InitialWaterMeterValue = kind == MeterKinds.Water ? baseline : null
        };
        context.Garages.Add(garage);
        await context.SaveChangesAsync();
        return garage;
    }

    private static UpsertGarageRequest Request(Garage garage) => new(garage.Number, garage.PeopleCount, garage.FloorCount, garage.OwnerId, garage.StartingBalance,
        garage.InitialWaterMeterValue, garage.InitialElectricityMeterValue, garage.Comment, Version: garage.Version, AccountingStartMonth: September, AccountingStartReason: "Учёт начат в сентябре");

    private static async Task CorrectAsync(GarageBalanceDbContext context, Garage garage)
    {
        var result = await DictionaryServiceTestFactory.Create(context, October).UpdateGarageAsync(garage.Id, Request(garage), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
    }
}
