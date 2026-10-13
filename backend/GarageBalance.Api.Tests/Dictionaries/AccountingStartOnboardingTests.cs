using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class AccountingStartOnboardingTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 13);

    [Fact]
    public async Task CompositeCreate_WithExplicitMonthCommitsAccrualPaymentAndFundTogether()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCreateAsync(database.Context, false);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CompositeCreateWithMonthAndPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCreateAsync(context, false);
    }

    [Fact]
    public async Task CompositeCreate_RollsBackAllInnerChangesAfterLaterPaymentFailure()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCreateAsync(database.Context, true);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CompositeFailureRollsBackReleasedSavepointAndFundPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCreateAsync(context, true);
    }

    private static async Task AssertCreateAsync(GarageBalanceDbContext context, bool fail)
    {
        var (setting, fund) = await SeedAsync(context);
        var initialAuditCount = await context.AuditEvents.CountAsync();
        var payments = new List<InitialGarageAnnualPaymentRequest> { new(setting.IncomeTypeId!.Value, 400) };
        if (fail) payments.Add(new(Guid.NewGuid(), 1));
        var service = Service(context);
        var result = await service.CreateWithAnnualPaymentsAsync(new(Request(September), 2026, payments), null, CancellationToken.None);
        if (fail)
        {
            Assert.Equal("garage_annual_payment_amount_invalid", result.ErrorCode);
            Assert.Empty(await context.Garages.ToListAsync());
            Assert.Empty(await context.Accruals.ToListAsync());
            Assert.Empty(await context.FinancialOperations.ToListAsync());
            Assert.Empty(await context.FundOperations.ToListAsync());
            Assert.Equal(initialAuditCount, await context.AuditEvents.CountAsync());
            Assert.Equal(0m, (await context.Funds.SingleAsync(row => row.Id == fund.Id)).Balance);
        }
        else
        {
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(September, result.Value!.AccountingStartMonth);
            var accrual = await context.Accruals.SingleAsync();
            Assert.Equal(September, accrual.AccountingMonth);
            Assert.Equal(1200m, accrual.Amount);
            Assert.Equal(400m, (await context.FinancialOperations.SingleAsync()).Amount);
            Assert.Equal(accrual.Id, (await context.FinancialOperations.SingleAsync()).TargetAccrualId);
            Assert.Equal(400m, (await context.Funds.SingleAsync(row => row.Id == fund.Id)).Balance);
            Assert.Equal(400m, (await context.AccrualPaymentAllocations.SingleAsync(row => row.IsActive)).Amount);
            Assert.DoesNotContain(context.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompositeUpdate_CorrectsMonthAtomicallyOrRestoresExistingHistory(bool fail)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertUpdateAsync(database.Context, fail);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CompositeUpdateCorrectsMonthAndRecordsPayment()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertUpdateAsync(context, false);
    }

    private static async Task AssertUpdateAsync(GarageBalanceDbContext context, bool fail)
    {
        var (setting, fund) = await SeedAsync(context);
        var created = await DictionaryServiceTestFactory.Create(context, October).CreateGarageAsync(Request(new(2026, 10, 1)), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var oldId = (await context.Accruals.SingleAsync()).Id;
        var auditCount = await context.AuditEvents.CountAsync();
        var payments = new List<InitialGarageAnnualPaymentRequest> { new(setting.IncomeTypeId!.Value, 400) };
        if (fail) payments.Add(new(Guid.NewGuid(), 1));
        var result = await Service(context).UpdateWithAnnualPaymentsAsync(created.Value!.Id,
            new(Request(September) with { Version = created.Value.Version, AccountingStartReason = "Начало учёта в сентябре" }, 2026, payments), null, CancellationToken.None);
        var garage = await context.Garages.SingleAsync();
        var accrual = await context.Accruals.SingleAsync();
        Assert.Equal(oldId, accrual.Id);
        if (fail)
        {
            Assert.Equal("garage_annual_payment_amount_invalid", result.ErrorCode);
            Assert.Equal(new DateOnly(2026, 10, 1), garage.AccountingStartMonth);
            Assert.Equal(new DateOnly(2026, 10, 1), accrual.AccountingMonth);
            Assert.Equal(0m, (await context.Funds.SingleAsync(row => row.Id == fund.Id)).Balance);
            Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
            Assert.Empty(await context.FinancialOperations.ToListAsync());
        }
        else
        {
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(September, garage.AccountingStartMonth);
            Assert.Equal(September, accrual.AccountingMonth);
            Assert.Equal(400m, (await context.FinancialOperations.SingleAsync()).Amount);
            Assert.Equal(400m, (await context.Funds.SingleAsync(row => row.Id == fund.Id)).Balance);
        }
    }

    private static GarageOnboardingService Service(GarageBalanceDbContext context) => new(
        DictionaryServiceTestFactory.Create(context, October), FinanceServiceTestFactory.Create(context, businessDate: October),
        new EfGarageOnboardingTransactionRunner(context), new TestBusinessDateProvider(October));

    private static UpsertGarageRequest Request(DateOnly start) => new("synthetic-composite-start", 1, 1, null, 0, null, null, null, AccountingStartMonth: start);

    private static async Task<(ChargeServiceSetting Setting, Fund Fund)> SeedAsync(GarageBalanceDbContext context)
    {
        foreach (var setting in context.ChargeServiceSettings) setting.IsArchived = true;
        var fund = new Fund { Name = "Synthetic composite month", IsSystem = false };
        var annual = new ChargeServiceSetting
        {
            Name = "Synthetic composite annual",
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 1,
            IncomeType = new IncomeType { Name = "Synthetic composite annual", Code = "synthetic_composite_annual", DestinationFund = fund },
            Tariff = new Tariff { Name = "Synthetic composite annual", CalculationBase = TariffCalculationBases.Fixed, Rate = 1200, EffectiveFrom = new(2026, 1, 1) }
        };
        context.ChargeServiceSettings.Add(annual);
        await context.SaveChangesAsync();
        return (annual, fund);
    }
}
