using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class LegacyAnnualParityRegressionTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Theory]
    [InlineData("batch", false)]
    [InlineData("card", false)]
    [InlineData("worksheet", false)]
    [InlineData("batch", true)]
    [InlineData("card", true)]
    [InlineData("worksheet", true)]
    public async Task JanuaryCancellationRemainsCanceledAfterSeptemberAccountingStart(string route, bool classified)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyCancellationAsync(database.Context, route, classified);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_LegacyJanuaryCancellationRemainsCanceledInCard()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyCancellationAsync(context, "card", false);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_LegacyJanuaryCancellationRemainsCanceledInWorksheet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyCancellationAsync(context, "worksheet", false);
    }

    private static async Task VerifyCancellationAsync(GarageBalanceDbContext context, string route, bool classified)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = January,
            AccountingYear = classified ? 2026 : null,
            IsAnnualAccrual = classified ? true : null,
            Source = AccrualSources.Regular,
            Amount = 1200
        };
        context.Add(old);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        var canceled = await service.CancelAccrualAsync(old.Id, new("Synthetic cancellation"), null, default);
        Assert.True(canceled.Succeeded, canceled.ErrorMessage);
        context.ChangeTracker.Clear();
        service = FinanceServiceTestFactory.Create(context, businessDate: November);
        if (route == "batch")
        {
            var result = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, January, null), null, default);
            Assert.False(result.Succeeded);
            Assert.Equal("regular_accruals_empty", result.ErrorCode);
        }
        else if (route == "card")
        {
            var result = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        else
        {
            var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        var rows = await context.Accruals.Where(row => row.GarageId == garage.Id).ToArrayAsync();
        Assert.True(rows.Length == 1 && rows[0].Id == old.Id && rows[0].IsCanceled,
            $"Expected only original canceled row. Route={route}, classified={classified}; actual: {Describe(rows)}");
    }

    [Theory]
    [InlineData("batch", false)]
    [InlineData("card", false)]
    [InlineData("worksheet", false)]
    [InlineData("batch", true)]
    [InlineData("card", true)]
    [InlineData("worksheet", true)]
    public async Task ActiveOctoberHistoryDoesNotDuplicateAfterScheduleMovesToNovember(string route, bool classified)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyShiftedMonthAsync(database.Context, route, classified);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_LegacyOctoberHistoryDoesNotDuplicateAfterScheduleMovesToNovember()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyShiftedMonthAsync(context, "worksheet", false);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ClassifiedHistoryPreservesCancellationAcrossAllRoutes()
    {
        foreach (var route in new[] { "batch", "card", "worksheet" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var context = database.CreateContext();
            await VerifyCancellationAsync(context, route, true);
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ClassifiedHistoryDoesNotDuplicateAfterScheduleMovesToNovember()
    {
        foreach (var route in new[] { "batch", "card", "worksheet" })
        {
            await using var database = await PostgreSqlTestDatabase.CreateAsync();
            await using var context = database.CreateContext();
            await VerifyShiftedMonthAsync(context, route, true);
        }
    }

    [Fact]
    public async Task ZeroCurrentCalculationDoesNotCancelShiftedLegacyAnnualHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyShiftedMonthAsync(database.Context, "worksheet", false, zeroCurrentCalculation: true);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ZeroCurrentCalculationDoesNotCancelShiftedLegacyAnnualHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyShiftedMonthAsync(context, "worksheet", false, zeroCurrentCalculation: true);
    }

    private static async Task VerifyShiftedMonthAsync(GarageBalanceDbContext context, string route, bool classified, bool zeroCurrentCalculation = false)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        setting.AccrualStartMonth = 10;
        setting.PaymentDueMonth = 10;
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            AccountingYear = classified ? 2026 : null,
            IsAnnualAccrual = classified ? true : null,
            Source = AccrualSources.Regular,
            Amount = 900
        };
        context.Add(old);
        await context.SaveChangesAsync();
        setting.AccrualStartMonth = 11;
        setting.PaymentDueMonth = 11;
        if (zeroCurrentCalculation)
        {
            setting.Tariff!.CalculationBase = TariffCalculationBases.People;
            garage.PeopleCount = 0;
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        if (route == "batch")
        {
            var result = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, November, null), null, default);
            Assert.False(result.Succeeded);
        }
        else if (route == "card")
        {
            var result = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        else
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default);
                Assert.True(result.Succeeded, result.ErrorMessage);
                context.ChangeTracker.Clear();
            }
        }
        var rows = await context.Accruals.Where(row => row.GarageId == garage.Id && !row.IsCanceled).ToArrayAsync();
        Assert.True(rows.Length == 1 && rows[0].Id == old.Id,
            $"Expected only original active obligation. Route={route}, classified={classified}; actual: {Describe(rows)}");
        if (!classified)
        {
            Assert.Equal(October, rows[0].AccountingMonth);
            Assert.Equal(900m, rows[0].Amount);
            Assert.Null(rows[0].AccountingYear);
            Assert.Null(rows[0].IsAnnualAccrual);
        }
    }

    [Theory]
    [InlineData("batch", false)]
    [InlineData("card", false)]
    [InlineData("worksheet", false)]
    [InlineData("batch", true)]
    [InlineData("card", true)]
    [InlineData("worksheet", true)]
    public async Task KnownNonannualHistoryRemainsMonthScoped(string route, bool canceled)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyKnownNonannualAsync(database.Context, route, canceled);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_KnownNonannualHistoryRemainsMonthScoped()
    {
        foreach (var route in new[] { "batch", "card", "worksheet" })
            foreach (var canceled in new[] { false, true })
            {
                await using var database = await PostgreSqlTestDatabase.CreateAsync();
                await using var context = database.CreateContext();
                await VerifyKnownNonannualAsync(context, route, canceled);
            }
    }

    private static async Task VerifyKnownNonannualAsync(GarageBalanceDbContext context, string route, bool canceled)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, October);
        setting.AccrualStartMonth = 11;
        setting.PaymentDueMonth = 11;
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = October,
            IsAnnualAccrual = false,
            Source = AccrualSources.Regular,
            Amount = 900
        };
        context.Add(old);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        if (canceled) Assert.True((await service.CancelAccrualAsync(old.Id, new("Synthetic nonannual cancellation"), null, default)).Succeeded);
        context.ChangeTracker.Clear();
        service = FinanceServiceTestFactory.Create(context, businessDate: November);
        if (route == "batch")
        {
            var repository = new EfAccrualRepository(context);
            Assert.Equal(0, await repository.CountActiveAnnualRegularForGenerationAsync(setting.IncomeTypeId!.Value, 2026, default));
            Assert.Empty(await repository.GetActiveAnnualRegularGarageIdsAsync(setting.IncomeTypeId.Value, 2026, default));
            var generated = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId.Value, setting.TariffId!.Value, November, null), null, default);
            Assert.True(generated.Succeeded, generated.ErrorMessage);
        }
        else if (route == "card")
            Assert.True((await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2026, null, default)).Succeeded);
        else
            Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default)).Succeeded);
        var rows = await context.Accruals.Where(row => row.GarageId == garage.Id).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        var preserved = Assert.Single(rows, row => row.Id == old.Id);
        Assert.Equal(canceled, preserved.IsCanceled);
        Assert.False(preserved.IsAnnualAccrual);
        Assert.Null(preserved.AccountingYear);
        Assert.Equal(October, preserved.AccountingMonth);
        Assert.Equal(900m, preserved.Amount);
        var annual = Assert.Single(rows, row => row.Id != old.Id);
        Assert.False(annual.IsCanceled);
        Assert.True(annual.IsAnnualAccrual);
        Assert.Equal(2026, annual.AccountingYear);
        Assert.Equal(November, annual.AccountingMonth);
        Assert.Equal(1200m, annual.Amount);
    }

    [Fact]
    public async Task MovedBatchPreservesKnownNonannualObligationAtDestinationMonth()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyMovedBatchCollisionAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_MovedBatchPreservesKnownNonannualObligationAtDestinationMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyMovedBatchCollisionAsync(context);
    }

    private static async Task VerifyMovedBatchCollisionAsync(GarageBalanceDbContext context)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = September,
            IsAnnualAccrual = false,
            Source = AccrualSources.Regular,
            Amount = 900
        };
        context.Add(old);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, January, null), null, default);
            Assert.False(result.Succeeded);
            Assert.Equal("regular_accruals_empty", result.ErrorCode);
        }
        var preserved = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id);
        Assert.Equal(old.Id, preserved.Id);
        Assert.Equal(September, preserved.AccountingMonth);
        Assert.Equal(900m, preserved.Amount);
        Assert.False(preserved.IsCanceled);
        Assert.False(preserved.IsAnnualAccrual);
        Assert.Null(preserved.AccountingYear);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreStartAnnualNormalizationPreservesCollidingHistory(bool storedYear, bool knownNonannualDestination)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyPreStartCollisionAsync(database.Context, storedYear, knownNonannualDestination);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PreStartAnnualNormalizationPreservesCollidingHistory()
    {
        foreach (var storedYear in new[] { false, true })
        {
            foreach (var knownNonannualDestination in new[] { false, true })
            {
                await using var database = await PostgreSqlTestDatabase.CreateAsync();
                await using var context = database.CreateContext();
                await VerifyPreStartCollisionAsync(context, storedYear, knownNonannualDestination);
            }
        }
    }

    private static async Task VerifyPreStartCollisionAsync(GarageBalanceDbContext context, bool storedYear, bool knownNonannualDestination)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        var annual = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = January,
            AccountingYear = storedYear ? 2026 : null,
            IsAnnualAccrual = true,
            Source = AccrualSources.Regular,
            Amount = 900
        };
        var destination = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = September,
            IsAnnualAccrual = knownNonannualDestination ? false : null,
            Source = AccrualSources.Regular,
            Amount = 300
        };
        context.AddRange(annual, destination);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, November), null, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Contains(result.Value!.Rows, row => row.CalculationIssue?.Contains("другое начисление") == true);
            context.ChangeTracker.Clear();
        }
        var rows = await context.Accruals.Where(row => row.GarageId == garage.Id).ToArrayAsync();
        Assert.Equal(2, rows.Length);
        var preservedAnnual = Assert.Single(rows, row => row.Id == annual.Id);
        Assert.False(preservedAnnual.IsCanceled);
        Assert.Equal(January, preservedAnnual.AccountingMonth);
        Assert.Equal(storedYear ? 2026 : (int?)null, preservedAnnual.AccountingYear);
        Assert.True(preservedAnnual.IsAnnualAccrual);
        Assert.Equal(900m, preservedAnnual.Amount);
        var preservedDestination = Assert.Single(rows, row => row.Id == destination.Id);
        Assert.False(preservedDestination.IsCanceled);
        Assert.Equal(September, preservedDestination.AccountingMonth);
        Assert.Null(preservedDestination.AccountingYear);
        Assert.Equal(knownNonannualDestination ? false : (bool?)null, preservedDestination.IsAnnualAccrual);
        Assert.Equal(300m, preservedDestination.Amount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PreStartAnnualHistoryKeepsIdentityAndRespectsClassificationAndPayments(bool paid, bool knownAnnual)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyPreStartHistoryAsync(database.Context, paid, knownAnnual);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PreStartAnnualHistoryKeepsIdentityAndRespectsClassificationAndPayments()
    {
        foreach (var paid in new[] { false, true })
            foreach (var knownAnnual in new[] { false, true })
            {
                await using var database = await PostgreSqlTestDatabase.CreateAsync();
                await using var context = database.CreateContext();
                await VerifyPreStartHistoryAsync(context, paid, knownAnnual);
            }
    }

    private static async Task VerifyPreStartHistoryAsync(GarageBalanceDbContext context, bool paid, bool knownAnnual)
    {
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        var old = new Accrual
        {
            Garage = garage,
            IncomeType = setting.IncomeType!,
            AccountingMonth = January,
            IsAnnualAccrual = knownAnnual ? true : null,
            Source = AccrualSources.Regular,
            Amount = 900
        };
        context.Add(old);
        if (paid)
        {
            var payment = new FinancialOperation
            {
                Garage = garage,
                IncomeType = setting.IncomeType,
                OperationKind = FinancialOperationKinds.Income,
                AccountingMonth = January,
                OperationDate = January.AddDays(10),
                Amount = 100
            };
            context.AddRange(payment, new AccrualPaymentAllocation { Accrual = old, FinancialOperation = payment, Amount = 100 });
        }
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.True((await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(November, November), null, default)).Succeeded);
            context.ChangeTracker.Clear();
        }
        var remaining = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id);
        Assert.Equal(old.Id, remaining.Id);
        Assert.False(remaining.IsCanceled);
        var normalize = knownAnnual && !paid;
        Assert.Equal(normalize ? September : January, remaining.AccountingMonth);
        Assert.Equal(normalize ? 1200m : 900m, remaining.Amount);
        Assert.Equal(normalize ? 2026 : (int?)null, remaining.AccountingYear);
        Assert.Equal(knownAnnual ? true : (bool?)null, remaining.IsAnnualAccrual);
        if (paid) Assert.Equal(100m, (await context.AccrualPaymentAllocations.SingleAsync(row => row.AccrualId == old.Id)).Amount);
    }

    [Fact]
    public async Task InitialOpenPeriodIncludesAnnualChargeCreatedBySameOpenCatchUp()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyFirstOpenAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_InitialOpenPeriodIncludesAnnualChargeCreatedBySameOpenCatchUp()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyFirstOpenAsync(context);
    }

    private static async Task VerifyFirstOpenAsync(GarageBalanceDbContext context)
    {
        var (garage, _) = await AnnualAccountingStartTests.SeedAsync(context, October);
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        // Period lookup stays read-only. The UI refreshes it after catch-up and,
        // when it changes, reloads the worksheet without recalculating a second time.
        var before = await service.GetFinancialReportPeriodAsync(new(garage.Id, null, null), default);
        Assert.True(before.Succeeded, before.ErrorMessage);
        Assert.Empty(await context.Accruals.ToArrayAsync());
        var result = await service.CalculateGarageIncomeWorksheetAsync(garage.Id,
            new(before.Value!.DefaultMonthFrom, before.Value.DefaultMonthTo), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var created = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && !row.IsCanceled);
        Assert.Equal(October, created.AccountingMonth);
        // The annual obligation remains visible and payable even with the stale filter.
        var visibleAnnual = Assert.Single(result.Value!.Rows, row => row.AnnualAccrualId == created.Id);
        Assert.Equal(1200m, visibleAnnual.AccrualAmount);
        Assert.Equal(1200m, visibleAnnual.PayableAmount);
        Assert.Equal(1200m, result.Value.ClosingDebt);
        var after = await service.GetFinancialReportPeriodAsync(new(garage.Id, null, null), default);
        Assert.True(after.Succeeded, after.ErrorMessage);
        Assert.Equal(October, after.Value!.DefaultMonthFrom);
        var refreshed = await service.GetGarageIncomeWorksheetAsync(garage.Id,
            new(after.Value.DefaultMonthFrom, after.Value.DefaultMonthTo, RefreshOnly: true), default);
        Assert.True(refreshed.Succeeded, refreshed.ErrorMessage);
        Assert.Equal(October, refreshed.Value!.MonthFrom);
        var refreshedAnnual = Assert.Single(refreshed.Value.Rows, row => row.AnnualAccrualId == created.Id);
        Assert.Equal(1200m, refreshedAnnual.AccrualAmount);
        Assert.Equal(1200m, refreshedAnnual.PayableAmount);
        Assert.Equal(1200m, refreshed.Value.ClosingDebt);
        Assert.Single(await context.Accruals.ToArrayAsync());
    }

    private static string Describe(IEnumerable<Accrual> rows) => string.Join("; ", rows.Select(row =>
        $"month={row.AccountingMonth:yyyy-MM}, year={row.AccountingYear}, annual={row.IsAnnualAccrual}, amount={row.Amount}, canceled={row.IsCanceled}"));
}
