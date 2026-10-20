using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionCorrectionTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correction_PreservesUnchangedPaidLaterMonth(bool changeBase)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertUnchangedPaidLaterMonthAsync(database.Context, changeBase);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CorrectionPreservesUnchangedPaidLaterMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertUnchangedPaidLaterMonthAsync(context, true);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("version-zero")]
    public async Task Correction_PreservesUnchangedPaidLegacyMonthWithoutCreatingASnapshot(string snapshotKind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertUnchangedPaidLaterMonthAsync(database.Context, true, snapshotKind);
    }

    private static async Task AssertUnchangedPaidLaterMonthAsync(GarageBalanceDbContext context, bool changeBase, string snapshotKind = "current")
    {
        var (garage, connection, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: new DateOnly(2026, 10, 31));
        var first = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, September, September.AddDays(29), 273m, null), null, CancellationToken.None);
        var nextMonth = September.AddMonths(1);
        var later = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, nextMonth, nextMonth.AddDays(30), 290m, null), null, CancellationToken.None);
        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(later.Succeeded, later.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && item.AccountingMonth == nextMonth);
        if (snapshotKind == "missing") accrual.CalculationDetailsJson = null;
        else if (snapshotKind == "version-zero") accrual.CalculationDetailsJson = RegularAccrualCalculator.Serialize(
            RegularAccrualCalculator.Deserialize(accrual.CalculationDetailsJson)! with { Version = 0 });
        var payment = await PayAsync(context, accrual);
        // PostgreSQL normalizes jsonb and timestamp precision when persisting a row. Compare
        // the stored representation on both sides, not the pre-save tracked values.
        await context.Entry(accrual).ReloadAsync();
        await context.Entry(payment).ReloadAsync();
        var originalDetails = accrual.CalculationDetailsJson;
        var originalComment = accrual.Comment;
        var originalUpdatedAt = accrual.UpdatedAtUtc;
        var originalReading = await context.MeterReadings.SingleAsync(item => item.Id == later.Value!.Id);
        await context.Entry(originalReading).ReloadAsync();
        var readingVersion = originalReading.Version;
        var readingUpdatedAt = originalReading.UpdatedAtUtc;
        var paymentVersion = payment.Version;
        var paymentUpdatedAt = payment.UpdatedAtUtc;

        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, changeBase ? 240m : 250m, September.AddDays(7), connection.Version, "Дата подключения уточнена по акту")), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var stored = await context.Accruals.SingleAsync(item => item.Id == accrual.Id);
        Assert.Equal(85m, stored.Amount);
        Assert.Equal(originalDetails, stored.CalculationDetailsJson);
        Assert.Equal(originalComment, stored.Comment);
        Assert.DoesNotContain("отменено", stored.Comment ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(originalUpdatedAt, stored.UpdatedAtUtc);
        var storedReading = await context.MeterReadings.SingleAsync(item => item.Id == later.Value!.Id);
        Assert.Equal((17m, readingVersion, readingUpdatedAt), (storedReading.Consumption, storedReading.Version, storedReading.UpdatedAtUtc));
        var allocation = await context.AccrualPaymentAllocations.SingleAsync(item => item.FinancialOperationId == payment.Id && item.AccrualId == accrual.Id && item.IsActive);
        Assert.Equal(85m, allocation.Amount);
        var storedPayment = await context.FinancialOperations.SingleAsync(item => item.Id == payment.Id);
        Assert.Equal((85m, paymentVersion, paymentUpdatedAt), (storedPayment.Amount, storedPayment.Version, storedPayment.UpdatedAtUtc));
        var firstAccrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && item.AccountingMonth == September);
        Assert.Equal(changeBase ? 165m : 115m, firstAccrual.Amount);
        Assert.Equal(September.AddDays(7), RegularAccrualCalculator.Deserialize(firstAccrual.CalculationDetailsJson)!.Lines[0].EffectiveFrom);
    }

    [Fact]
    public async Task Correction_RecalculatesPaidFirstMonthAndKeepsPayment()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, connection, setting) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context);
        var reading = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, September, September.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId);
        var payment = await PayAsync(context, accrual);
        await context.Entry(payment).ReloadAsync();
        var paymentVersion = payment.Version;
        var version = connection.Version;
        var snapshot = accrual.CalculationDetailsJson;
        var auditCount = await context.AuditEvents.CountAsync();
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 240m, September.AddDays(7), connection.Version, "Уточнение акта")), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var saved = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync(item => item.GarageId == garage.Id);
        Assert.NotEqual(version, saved.Version);
        Assert.Equal(September.AddDays(7), saved.ConnectedOn);
        Assert.Equal(240m, saved.BaselineMeterDevice!.InitialValue);
        // The paid first month follows the corrected base (273 - 240) and the payment is not touched.
        var savedAccrual = await context.Accruals.SingleAsync(item => item.Id == accrual.Id);
        Assert.Equal(165m, savedAccrual.Amount);
        Assert.NotEqual(snapshot, savedAccrual.CalculationDetailsJson);
        Assert.Equal(September.AddDays(7), RegularAccrualCalculator.Deserialize(savedAccrual.CalculationDetailsJson)!.Lines[0].EffectiveFrom);
        Assert.True(await context.AuditEvents.CountAsync() > auditCount);
        var storedReading = await context.MeterReadings.SingleAsync(item => item.Id == reading.Value!.Id);
        Assert.Equal((240m, 33m), (storedReading.PreviousValue, storedReading.Consumption));
        var storedPayment = await context.FinancialOperations.SingleAsync(item => item.Id == payment.Id);
        Assert.Equal((115m, false, paymentVersion), (storedPayment.Amount, storedPayment.IsCanceled, storedPayment.Version));
        var allocations = await context.AccrualPaymentAllocations.Where(item => item.AccrualId == accrual.Id && item.IsActive).ToListAsync();
        Assert.Equal(115m, allocations.Sum(item => item.Amount));
    }

    [Fact]
    public async Task DateCorrection_RejectsMissingTariffForNewlyIncludedDays()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, connection, setting) = await SeedAsync(context, tariffStartsOnConnection: true);
        var finance = FinanceServiceTestFactory.Create(context);
        var reading = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Water, September, September.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId);
        var snapshot = accrual.CalculationDetailsJson;
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 250m, September.AddDays(7), connection.Version, "Уточнение даты")), null, CancellationToken.None);
        Assert.Equal("meter_connection_calculation_invalid", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(September.AddDays(9), (await context.GarageMeterConnections.SingleAsync(item => item.GarageId == garage.Id)).ConnectedOn);
        Assert.Equal(snapshot, (await context.Accruals.SingleAsync(item => item.Id == accrual.Id)).CalculationDetailsJson);
    }

    [Fact]
    public async Task LegacyPaidAccrual_RecalculatesChangedConsumptionEvenWhenMoneyRoundsToTheSameAmount()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, connection, setting) = await SeedAsync(context);
        setting.Tariff!.Rate = 0.01m;
        await context.SaveChangesAsync();
        var reading = await FinanceServiceTestFactory.Create(context).CreateMeterReadingAsync(
            new(garage.Id, MeterKinds.Water, September, September.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId);
        Assert.Equal(0.23m, accrual.Amount);
        accrual.CalculationDetailsJson = null;
        var payment = await PayAsync(context, accrual);
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 249.999m, connection.ConnectedOn, connection.Version, "Уточнение тысячной доли показания")), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Equal(23.001m, (await context.MeterReadings.SingleAsync(item => item.Id == reading.Value!.Id)).Consumption);
        // The changed consumption is recorded in a fresh snapshot even though the money rounds the same.
        var stored = await context.Accruals.SingleAsync(item => item.Id == accrual.Id);
        Assert.Equal(0.23m, stored.Amount);
        Assert.NotNull(stored.CalculationDetailsJson);
        Assert.Equal(23.001m, RegularAccrualCalculator.Deserialize(stored.CalculationDetailsJson)!.MeterConsumption);
        var storedPayment = await context.FinancialOperations.SingleAsync(item => item.Id == payment.Id);
        Assert.Equal((0.23m, false), (storedPayment.Amount, storedPayment.IsCanceled));
        Assert.Equal(0.23m, (await context.AccrualPaymentAllocations.Where(item => item.AccrualId == accrual.Id && item.IsActive).ToListAsync()).Sum(item => item.Amount));
    }

    private static async Task<(Garage Garage, GarageMeterConnection Connection, ChargeServiceSetting Setting)> SeedAsync(
        GarageBalanceDbContext context, bool tariffStartsOnConnection = false)
    {
        var garage = new Garage { Number = "connection-correction-synthetic", RegisteredOn = new DateOnly(2026, 1, 1), InitialMeterReadingMonth = new DateOnly(2025, 12, 1) };
        var income = new IncomeType { Code = "connection_correction", Name = "Вода по отдельному подключению" };
        var tariff = new Tariff
        {
            Name = "Тариф для проверки подключения",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 5m,
            EffectiveFrom = tariffStartsOnConnection ? September.AddDays(9) : new DateOnly(2026, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Вода для проверки подключения",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.Water,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            IncomeType = income,
            Tariff = tariff,
            AppliesToSelectedGarages = true,
            GarageIds = [garage.Id]
        };
        context.AddRange(garage, income, tariff, setting);
        await context.SaveChangesAsync();
        var applied = await FinanceServiceTestFactory.Create(context).ApplyStartValuesAsync(garage,
            [new(MeterKinds.Water, 250m, September.AddDays(9))], null, CancellationToken.None);
        Assert.True(applied.Succeeded, applied.ErrorMessage);
        await using var lease = applied.Value!;
        await context.SaveChangesAsync();
        return (garage, await context.GarageMeterConnections.SingleAsync(item => item.GarageId == garage.Id), setting);
    }

    private static async Task<FinancialOperation> PayAsync(GarageBalanceDbContext context, Accrual accrual)
    {
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = accrual.AccountingMonth.AddDays(20),
            AccountingMonth = accrual.AccountingMonth,
            GarageId = accrual.GarageId,
            IncomeTypeId = accrual.IncomeTypeId,
            TargetAccrualId = accrual.Id,
            Amount = accrual.Amount
        };
        context.AddRange(payment, new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = accrual.Amount });
        await context.SaveChangesAsync();
        return payment;
    }

    private static UpsertGarageRequest Request(Garage garage, UpsertGarageMeterStartValueRequest value) =>
        new(garage.Number, garage.PeopleCount, garage.FloorCount, garage.OwnerId, garage.StartingBalance,
            garage.InitialWaterMeterValue, garage.InitialElectricityMeterValue, garage.Comment, garage.Version,
            garage.StartingOverdueDebt, [value]);
}
