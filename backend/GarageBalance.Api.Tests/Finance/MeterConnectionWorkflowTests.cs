using System.Text.Json;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionWorkflowTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Fact]
    public async Task LateConnection_CalculatesFirstAndFollowingMonthWithoutChangingOldService()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertLateConnectionAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task LateConnection_PostgreSqlPreservesHistoryAndUsesConnectedDays()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertLateConnectionAsync(context);
    }

    private static async Task AssertLateConnectionAsync(GarageBalanceDbContext context)
    {
        var (garage, setting) = await SeedAsync(context);
        var oldIncome = new IncomeType { Code = "historic_water", Name = "Прежняя вода" };
        var oldReading = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = September.AddMonths(-1),
            ReadingDate = September.AddDays(-1),
            PreviousValue = 50m,
            CurrentValue = 60m,
            Consumption = 10m
        };
        var oldAccrual = new Accrual
        {
            Garage = garage,
            IncomeType = oldIncome,
            AccountingMonth = September.AddMonths(-1),
            DueDate = September,
            OverdueFromDate = September,
            Source = AccrualSources.Regular,
            Amount = 77m,
            CalculationDetailsJson = "{\"historical\":true}"
        };
        context.AddRange(oldIncome, oldReading, oldAccrual);
        await context.SaveChangesAsync();
        var oldSnapshot = JsonSerializer.Serialize(new
        {
            oldReading.PreviousValue,
            oldReading.CurrentValue,
            oldReading.Consumption,
            oldReading.Version,
            oldAccrual.Amount,
            oldAccrual.CalculationDetailsJson,
            oldAccrual.UpdatedAtUtc,
            garage.InitialMeterReadingMonth,
            garage.InitialWaterMeterValue,
            garage.InitialElectricityMeterValue
        });
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 10, 15));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, 250m);

        var first = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, September, September.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(23m, first.Value!.Consumption);
        Assert.False(first.Value.HasGapWarning);
        var firstAccrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && item.AccountingMonth == September);
        Assert.Equal(115m, firstAccrual.Amount);
        Assert.Equal(September.AddDays(9), RegularAccrualCalculator.Deserialize(firstAccrual.CalculationDetailsJson)!.Lines[0].EffectiveFrom);

        var next = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, September.AddMonths(1), September.AddMonths(2).AddDays(-1), 290m, null), null, CancellationToken.None);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.Equal(17m, next.Value!.Consumption);
        Assert.Equal(85m, (await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && item.AccountingMonth == September.AddMonths(1))).Amount);
        Assert.Equal(oldSnapshot, JsonSerializer.Serialize(new
        {
            oldReading.PreviousValue,
            oldReading.CurrentValue,
            oldReading.Consumption,
            oldReading.Version,
            oldAccrual.Amount,
            oldAccrual.CalculationDetailsJson,
            oldAccrual.UpdatedAtUtc,
            garage.InitialMeterReadingMonth,
            garage.InitialWaterMeterValue,
            garage.InitialElectricityMeterValue
        }));
        Assert.False(await context.Accruals.AnyAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && item.AccountingMonth < September));
    }

    [Theory]
    [InlineData(false, false, "meter_connection_required")]
    [InlineData(true, false, "meter_start_value_required")]
    [InlineData(true, true, "meter_reading_outside_connection")]
    public async Task UnknownBaseAndReadingBeforeConnection_CannotCreateReadingOrAccrual(bool connect, bool provideBase, string error)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 9, 30));
        if (connect) await ConnectAsync(context, finance, garage, setting.MeterKind!, provideBase ? 0m : null);
        var result = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, September,
            provideBase ? September.AddDays(8) : September.AddDays(29), 10m, null), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(error, result.ErrorCode);
        Assert.False(await context.MeterReadings.AnyAsync(item => item.GarageId == garage.Id));
        Assert.False(await context.Accruals.AnyAsync(item => item.GarageId == garage.Id));
    }

    [Fact]
    public async Task ExplicitZero_IsAValidBase()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await SeedAsync(database.Context);
        var finance = FinanceServiceTestFactory.Create(database.Context, businessDate: new DateOnly(2026, 9, 30));
        await ConnectAsync(database.Context, finance, garage, setting.MeterKind!, 0m);
        var result = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, September, September.AddDays(29), 1m, null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1m, result.Value!.Consumption);
        Assert.Equal(5m, (await database.Context.Accruals.SingleAsync()).Amount);
    }

    [Fact]
    public async Task SeveralReplacementsInFirstMonth_AndCancellation_PreserveConnectionBase()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 9, 30));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, 250m);
        var first = await finance.ReplaceMeterDeviceAsync(new(garage.Id, setting.MeterKind!, September, September.AddDays(14),
            "synthetic-first", 0m, 5m, 260m, "Первая замена"), null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(15m, first.Value!.Reading.Consumption);
        var second = await finance.ReplaceMeterDeviceAsync(new(garage.Id, setting.MeterKind!, September, September.AddDays(19),
            "synthetic-second", 100m, 106m, 7m, "Вторая замена", first.Value.Reading.Id, first.Value.Reading.Version), null, CancellationToken.None);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(23m, second.Value!.Reading.Consumption);
        Assert.Equal(115m, (await context.Accruals.SingleAsync()).Amount);
        var canceled = await finance.CancelMeterDeviceReplacementAsync(second.Value.Device.Id,
            new(second.Value.Device.Version, second.Value.Reading.Version, September, "Отмена второй замены"), null, CancellationToken.None);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        Assert.Equal(15m, canceled.Value!.Reading.Consumption);
        Assert.Equal(75m, (await context.Accruals.SingleAsync()).Amount);
        var connection = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        Assert.Equal(250m, connection.BaselineMeterDevice!.InitialValue);
        Assert.Equal(September.AddDays(9), connection.ConnectedOn);
    }

    [Fact]
    public async Task EditAfterReplacement_UsesNewDeviceBaseAndPreservesOldConsumption()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 9, 30));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, 250m);
        var replacement = await finance.ReplaceMeterDeviceAsync(new(garage.Id, setting.MeterKind!, September,
            September.AddDays(14), "synthetic-edit", 0m, 5m, 260m, "Замена"), null, CancellationToken.None);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);

        var edited = await finance.UpdateMeterReadingAsync(replacement.Value!.Reading.Id,
            new(garage.Id, setting.MeterKind!, September, September.AddDays(29), 6m, null), null, CancellationToken.None);

        Assert.True(edited.Succeeded, edited.ErrorMessage);
        Assert.Equal(0m, edited.Value!.PreviousValue);
        Assert.Equal(16m, edited.Value.Consumption);
        Assert.Equal(80m, (await context.Accruals.SingleAsync()).Amount);
        Assert.Equal(250m, (await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync()).BaselineMeterDevice!.InitialValue);

        var outsideDevicePeriod = await finance.UpdateMeterReadingAsync(edited.Value.Id,
            new(garage.Id, setting.MeterKind!, September, September.AddDays(13), 7m, null), null, CancellationToken.None);
        Assert.Equal("meter_reading_outside_device_period", outsideDevicePeriod.ErrorCode);
        Assert.Equal(16m, (await context.MeterReadings.SingleAsync()).Consumption);
    }

    [Fact]
    public async Task HistoricalEditBeforeFollowingReplacement_DoesNotCompareDifferentDeviceReadings()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 10, 31));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, 250m);
        var first = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, September,
            September.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        var replacement = await finance.ReplaceMeterDeviceAsync(new(garage.Id, setting.MeterKind!, September.AddMonths(1),
            September.AddMonths(1).AddDays(14), "synthetic-following", 0m, 5m, 280m, "Замена"), null, CancellationToken.None);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);

        var edited = await finance.CorrectHistoricalMeterReadingAsync(first.Value!.Id,
            new(September.AddDays(29), 274m, null, "Уточнение показания до замены", first.Value.Version), null, CancellationToken.None);

        Assert.True(edited.Succeeded, edited.ErrorMessage);
        Assert.Equal(24m, edited.Value!.Consumption);
        var following = await context.MeterReadings.SingleAsync(item => item.Id == replacement.Value!.Reading.Id);
        Assert.Equal(11m, following.Consumption);
        Assert.Equal(6m, following.PreviousDeviceConsumption);

        var afterRemoval = await finance.CorrectHistoricalMeterReadingAsync(edited.Value.Id,
            new(September.AddMonths(1).AddDays(15), 275m, null, "Проверка даты после снятия", edited.Value.Version), null, CancellationToken.None);
        Assert.Equal("meter_reading_outside_device_period", afterRemoval.ErrorCode);
        Assert.Equal(24m, (await context.MeterReadings.SingleAsync(item => item.Id == edited.Value.Id)).Consumption);
    }

    [Fact]
    public async Task ConnectionCard_ReportsMissingTariffWithoutTreatingUnknownPriceAsZero()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: September.AddMonths(1));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, null);
        Assert.False(Assert.Single(await finance.GetStartValuesAsync(garage, CancellationToken.None)).RequiresTariff);
        setting.Tariff!.EffectiveFrom = September.AddDays(10);
        await context.SaveChangesAsync();
        Assert.True(Assert.Single(await finance.GetStartValuesAsync(garage, CancellationToken.None)).RequiresTariff);
        setting.Tariff.EffectiveFrom = September.AddDays(9);
        await context.SaveChangesAsync();
        Assert.False(Assert.Single(await finance.GetStartValuesAsync(garage, CancellationToken.None)).RequiresTariff);
        setting.Tariff = null;
        setting.TariffId = null;
        await context.SaveChangesAsync();
        Assert.True(Assert.Single(await finance.GetStartValuesAsync(garage, CancellationToken.None)).RequiresTariff);
        Assert.Empty(await context.Accruals.ToArrayAsync());
    }

    [Fact]
    public async Task GenerationAndRecalculationPreview_UseTheSameConnectedPeriod()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 9, 30));
        await ConnectAsync(context, finance, garage, setting.MeterKind!, 250m);
        var connection = await context.GarageMeterConnections.SingleAsync();
        context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            MeterDeviceId = connection.BaselineMeterDeviceId,
            AccountingMonth = September,
            ReadingDate = September.AddDays(29),
            PreviousValue = 250m,
            CurrentValue = 273m,
            Consumption = 23m
        });
        await context.SaveChangesAsync();
        var generated = await finance.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, September, null), null, CancellationToken.None);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        Assert.Equal(115m, generated.Value!.TotalAmount);
        var preview = await finance.PreviewRegularAccrualRecalculationAsync(new(setting.IncomeTypeId.Value, setting.TariffId.Value, September), CancellationToken.None);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        Assert.Equal(115m, preview.Value!.ProposedTotal);
    }

    private static async Task ConnectAsync(GarageBalanceDbContext context, FinanceService finance, Garage garage, string kind, decimal? value)
    {
        var applied = await finance.ApplyStartValuesAsync(garage, [new(kind, value, September.AddDays(9))], null, CancellationToken.None);
        Assert.True(applied.Succeeded, applied.ErrorMessage);
        await using var heldLocks = applied.Value!;
        await context.SaveChangesAsync();
    }

    private static async Task<(Garage Garage, ChargeServiceSetting Setting)> SeedAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage { Number = "synthetic-connection", RegisteredOn = new DateOnly(2026, 1, 1), InitialMeterReadingMonth = new DateOnly(2025, 12, 1), InitialWaterMeterValue = 50m, InitialElectricityMeterValue = 100m };
        var income = new IncomeType { Code = "late_meter", Name = "Подключённая услуга" };
        var tariff = new Tariff { Name = "Синтетический тариф", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 5m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Новая счётчиковая услуга",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            UnitName = "кВт·ч",
            IncomeType = income,
            Tariff = tariff,
            AppliesToSelectedGarages = true,
            GarageIds = [garage.Id]
        };
        context.AddRange(garage, income, tariff, setting);
        await context.SaveChangesAsync();
        return (garage, setting);
    }
}
