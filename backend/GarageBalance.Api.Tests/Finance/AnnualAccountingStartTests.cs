using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AnnualAccountingStartTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    [Theory]
    [InlineData(9, 2027)]
    [InlineData(12, 2027)]
    public async Task ExplicitStart_UsesBillingMonthForAnnualDeadline(int month, int dueYear)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, _) = await SeedAsync(database.Context, new(2026, month, 1));
        var result = await FinanceServiceTestFactory.Create(database.Context, businessDate: new(2026, 12, 20))
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(new(2026, month, 1), new(2026, month, 1)), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = await database.Context.Accruals.SingleAsync();
        Assert.Equal(new DateOnly(dueYear, 6, 30), accrual.DueDate);
        Assert.Equal(new DateOnly(dueYear, 7, 31), accrual.OverdueFromDate);
        Assert.Equal(1200m, accrual.Amount);
        Assert.Equal(2026, accrual.AccountingYear);
    }

    [Fact]
    public async Task Recalculation_MovesOnlyUnpaidAnnualMonthAndAuditsDatesEvenWhenAmountIsUnchanged()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertAnnualCorrectionAsync(database.Context);
    }

    [Fact]
    public async Task ReadOnlyAnnualPlan_UsesSelectedMonthAndDeadlineWithoutCreatingAccrual()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, _) = await SeedAsync(database.Context, September);
        var result = await FinanceServiceTestFactory.Create(database.Context, businessDate: October)
            .GetGarageAnnualPaymentsAsync(garage.Id, 2026, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(September, item.AccountingMonth);
        Assert.Equal(new DateOnly(2027, 6, 30), item.DueDate);
        Assert.Null(item.AccrualId);
        Assert.False(item.CanRecordPayment);
        Assert.Empty(await database.Context.Accruals.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualCorrectionKeepsIdentityAndUsesBillingDeadline()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertAnnualCorrectionAsync(context);
    }

    private static async Task AssertAnnualCorrectionAsync(GarageBalanceDbContext context)
    {
        var (garage, setting) = await SeedAsync(context, September);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            Tariff = setting.Tariff,
            AccountingMonth = October,
            AccountingYear = 2026,
            Amount = 1200,
            DueDate = new(2026, 6, 30),
            OverdueFromDate = new(2026, 7, 31),
            Source = AccrualSources.Regular
        };
        context.Accruals.Add(old);
        await context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(context, businessDate: October)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, October), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var changed = await context.Accruals.SingleAsync();
        Assert.Equal(old.Id, changed.Id);
        Assert.Equal(September, changed.AccountingMonth);
        Assert.Equal(new DateOnly(2027, 6, 30), changed.DueDate);
        Assert.Equal(1200m, changed.Amount);
        var audit = Assert.Single(await context.AuditEvents.Where(row => row.Action == "finance.regular_accrual_recalculated_for_garage_worksheet").ToListAsync());
        using var metadata = System.Text.Json.JsonDocument.Parse(audit.MetadataJson!);
        Assert.Contains("30.06.2027", metadata.RootElement.GetProperty("newValue").GetString());
        Assert.Contains("09.2026", metadata.RootElement.GetProperty("newValue").GetString());
        Assert.DoesNotContain("[секрет скрыт]", metadata.RootElement.GetProperty("newValue").GetString());
    }

    [Theory]
    [InlineData(25)]
    [InlineData(1200)]
    public async Task Recalculation_KeepsPaidOrPartiallyPaidAnnualDates(int paid)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context, September);
        var oldDate = new DateOnly(2026, 6, 30);
        var accrual = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            AccountingYear = 2026,
            Amount = 1200,
            DueDate = oldDate,
            OverdueFromDate = oldDate.AddDays(31),
            Source = AccrualSources.Regular
        };
        var payment = new FinancialOperation
        {
            Garage = garage,
            IncomeType = setting.IncomeType,
            OperationKind = FinancialOperationKinds.Income,
            AccountingMonth = October,
            OperationDate = October.AddDays(10),
            Amount = paid
        };
        context.AddRange(accrual, payment, new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = paid });
        await context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(context, businessDate: October)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, October), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var unchanged = await context.Accruals.SingleAsync();
        Assert.Equal(October, unchanged.AccountingMonth);
        Assert.Equal(oldDate, unchanged.DueDate);
        Assert.Equal(1200m, unchanged.Amount);
        Assert.Empty(await context.AuditEvents.Where(row => row.Action == "finance.regular_accrual_recalculated_for_garage_worksheet").ToListAsync());
    }

    [Fact]
    public async Task Recalculation_ExplainsCollidingHistoryWithoutCancelingOrDuplicatingRecords()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context, September);
        var monthly = new Accrual { Garage = garage, IncomeType = setting.IncomeType!, AccountingMonth = September, Amount = 25, Source = AccrualSources.Regular };
        var annual = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            AccountingYear = 2026,
            Amount = 1200,
            DueDate = new(2026, 6, 30),
            OverdueFromDate = new(2026, 7, 31),
            Source = AccrualSources.Regular
        };
        context.AddRange(monthly, annual);
        await context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(context, businessDate: October)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, October), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, await context.Accruals.CountAsync());
        Assert.False(monthly.IsCanceled);
        Assert.False(annual.IsCanceled);
        Assert.Equal(October, annual.AccountingMonth);
        Assert.Equal(1200m, annual.Amount);
        Assert.Contains(result.Value!.Rows, row => row.CalculationIssue != null && row.CalculationIssue.Contains("другое начисление", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(2026, 12)]
    [InlineData(2027, 1)]
    public async Task FutureStart_PreventsDefaultAnnualCreationBeforeAccountingBegins(int year, int month)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context, new(year, month, 1));
        var service = FinanceServiceTestFactory.Create(context, businessDate: October);
        var annual = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, CancellationToken.None);
        Assert.True(annual.Succeeded, annual.ErrorMessage);
        Assert.Empty(await context.Accruals.ToListAsync());
        var batch = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, October, null), null, CancellationToken.None);
        Assert.False(batch.Succeeded);
        Assert.Equal("regular_accruals_empty", batch.ErrorCode);
        Assert.Contains(new DateOnly(year, month, 1).ToString("MM.yyyy"), batch.ErrorMessage);
        Assert.Empty(await context.Accruals.ToListAsync());
        if (year > 2026) Assert.Empty(annual.Value!.Items);
    }

    private static async Task<(Garage Garage, ChargeServiceSetting Setting)> SeedAsync(GarageBalanceDbContext context, DateOnly start)
    {
        foreach (var setting in context.ChargeServiceSettings) setting.IsArchived = true;
        var garage = new Garage { Number = "synthetic-annual-start", RegisteredOn = October.AddDays(12), AccountingStartMonth = start, PeopleCount = 1, FloorCount = 1 };
        var annual = new ChargeServiceSetting
        {
            Name = "Synthetic annual start",
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            PaymentDueMonth = 6,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            IncomeType = new IncomeType { Name = "Synthetic annual start", Code = "synthetic_annual_start" },
            Tariff = new Tariff { Name = "Synthetic annual start", CalculationBase = TariffCalculationBases.Fixed, Rate = 1200, EffectiveFrom = new(2026, 1, 1) }
        };
        context.AddRange(garage, annual);
        await context.SaveChangesAsync();
        return (garage, annual);
    }
}
