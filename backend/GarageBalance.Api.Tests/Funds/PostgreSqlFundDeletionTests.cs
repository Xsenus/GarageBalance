using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlFundDeletionTests
{
    [PostgreSqlFact]
    public async Task ArchivedFund_RejectsHistoricalMutationsWithoutChangingBalancesOrAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid fundId;
        Guid depositId;
        Guid canceledId;
        Guid withdrawalId;
        await using (var context = database.CreateContext())
        {
            var fund = new Fund { Name = "Архивный контроль", NormalizedName = "АРХИВНЫЙ КОНТРОЛЬ", IsSystem = false };
            context.AddRange(fund, new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                Amount = 200m,
                OperationDate = new DateOnly(2026, 9, 7),
                AccountingMonth = new DateOnly(2026, 9, 1)
            });
            await context.SaveChangesAsync();
            fundId = fund.Id;
            var service = CreateService(context);
            var deposit = await service.CreateOperationAsync(fundId, new("deposit", 80m, "Распределение"), null, CancellationToken.None);
            Assert.True(deposit.Succeeded, deposit.ErrorMessage);
            depositId = deposit.Value!.Id;
            var canceled = await service.CreateOperationAsync(fundId, new("deposit", 10m, "Отменяемое распределение"), null, CancellationToken.None);
            Assert.True(canceled.Succeeded, canceled.ErrorMessage);
            canceledId = canceled.Value!.Id;
            Assert.True((await service.CancelOperationAsync(canceledId, new("Отмена"), null, CancellationToken.None)).Succeeded);
            var withdrawal = await service.CreateOperationAsync(fundId, new("withdraw", 40m, "Возврат"), null, CancellationToken.None);
            Assert.True(withdrawal.Succeeded, withdrawal.ErrorMessage);
            withdrawalId = withdrawal.Value!.Id;
            Assert.True((await service.DeleteFundAsync(fundId, new("Закрытие", fund.Version), null, CancellationToken.None)).Succeeded);
        }

        await using var verification = database.CreateContext();
        var beforeOperations = await verification.FundOperations.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        var beforeAudit = await verification.AuditEvents.CountAsync();
        var before = await CreateService(verification).GetReconciliationAsync(CancellationToken.None);
        Assert.True(before.IsReconciled);
        Assert.Equal(200m, before.UnallocatedTotal);
        // Повторные запросы из устаревшего окна после архивирования тоже должны быть безопасны.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var mutationContext = database.CreateContext();
            var service = CreateService(mutationContext);
            var results = new[]
            {
                await service.UpdateOperationAsync(depositId, new(81m, "Уточнение"), null, CancellationToken.None),
                await service.UpdateOperationAsync(withdrawalId, new(39m, "Уточнение возврата"), null, CancellationToken.None),
                await service.CancelOperationAsync(withdrawalId, new("Отмена возврата"), null, CancellationToken.None),
                await service.RestoreOperationAsync(canceledId, null, CancellationToken.None)
            };
            Assert.All(results, result =>
            {
                Assert.False(result.Succeeded);
                Assert.Equal("fund_archived", result.ErrorCode);
            });
            Assert.False((await service.CreateOperationAsync(fundId, new("deposit", 1m, "Обратная операция"), null, CancellationToken.None)).Succeeded);
        }

        verification.ChangeTracker.Clear();
        var after = await CreateService(verification).GetReconciliationAsync(CancellationToken.None);
        Assert.Equal(before, after);
        var savedFund = await verification.Funds.AsNoTracking().SingleAsync(item => item.Id == fundId);
        Assert.True(savedFund.IsArchived);
        Assert.Equal(0m, savedFund.Balance);
        var historyService = CreateService(verification);
        Assert.All(await historyService.GetOperationsAsync(100, true, CancellationToken.None), item => Assert.True(item.IsFundArchived));
        Assert.All((await historyService.GetOperationsPageAsync(0, 100, true, CancellationToken.None)).Items, item => Assert.True(item.IsFundArchived));
        Assert.Equal(beforeAudit, await verification.AuditEvents.CountAsync());
        var afterOperations = await verification.FundOperations.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(beforeOperations), System.Text.Json.JsonSerializer.Serialize(afterOperations));
    }

    [PostgreSqlFact]
    public async Task DeleteFund_DetachesCurrentSettingsButPreservesHistoricalDocumentsAndReconciliation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = new Fund { Name = "Удаляемый", NormalizedName = "УДАЛЯЕМЫЙ", IsSystem = false };
        var income = new IncomeType { Name = "Проверочное поступление", DestinationFund = fund };
        var supplier = new Supplier
        {
            Name = "Проверочный поставщик",
            Group = new SupplierGroup { Name = "Проверочная группа" },
            ExpenseFund = fund,
            IsArchived = true
        };
        var expense = new ExpenseType { Name = "Проверочный расход" };
        var month = new DateOnly(2026, 8, 1);
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Expense,
            Supplier = supplier,
            ExpenseType = expense,
            ExpenseFund = fund,
            Amount = 20m,
            OperationDate = month,
            AccountingMonth = month,
            ExpensePaymentSource = "bank"
        };
        var accrual = new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseType = expense,
            ExpenseFund = fund,
            Amount = 30m,
            AccountingMonth = month,
            Source = "manual"
        };
        context.AddRange(fund, income, supplier, expense, payment, accrual,
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                IncomeType = income,
                Amount = 100m,
                OperationDate = month,
                AccountingMonth = month
            });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        Assert.True((await service.CreateOperationAsync(fund.Id,
            new CreateFundOperationRequest("deposit", 50m, "Проверка"), null, CancellationToken.None)).Succeeded);
        var before = await service.GetReconciliationAsync(CancellationToken.None);
        var storedPayment = await context.FinancialOperations.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
        var paymentVersion = storedPayment.Version;

        Assert.True((await service.DeleteFundAsync(fund.Id,
            new DeleteFundRequest("Закрытие", fund.Version), null, CancellationToken.None)).Succeeded);
        context.ChangeTracker.Clear();

        var after = await service.GetReconciliationAsync(CancellationToken.None);
        Assert.True(before.IsReconciled);
        Assert.True(after.IsReconciled);
        Assert.Equal(before.CashAndBankTotal, after.CashAndBankTotal);
        Assert.Equal(before.NamedFundTotal - 50m, after.NamedFundTotal);
        Assert.Equal(before.UnallocatedTotal + 50m, after.UnallocatedTotal);
        Assert.Null((await context.Suppliers.SingleAsync(item => item.Id == supplier.Id)).ExpenseFundId);
        Assert.Null((await context.IncomeTypes.SingleAsync(item => item.Id == income.Id)).DestinationFundId);
        Assert.Equal(fund.Id, (await context.SupplierAccruals.SingleAsync(item => item.Id == accrual.Id)).ExpenseFundId);
        var savedPayment = await context.FinancialOperations.SingleAsync(item => item.Id == payment.Id);
        Assert.Equal(fund.Id, savedPayment.ExpenseFundId);
        Assert.Equal(20m, savedPayment.Amount);
        Assert.Equal(paymentVersion, savedPayment.Version);
        Assert.True((await context.Funds.SingleAsync(item => item.Id == fund.Id)).IsArchived);
        Assert.Equal(2, await context.FundOperations.CountAsync(item => item.FundId == fund.Id));
    }

    [PostgreSqlFact]
    public async Task DeleteFund_ConcurrentSupplierChangeRollsBackTransferArchiveAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = new Fund { Name = "Конкурентный", NormalizedName = "КОНКУРЕНТНЫЙ", IsSystem = false, Balance = 10m };
        var supplier = new Supplier
        {
            Name = "Поставщик",
            Group = new SupplierGroup { Name = "Группа" },
            ExpenseFund = fund
        };
        context.AddRange(fund, supplier);
        await context.SaveChangesAsync();
        await using (var concurrentContext = database.CreateContext())
        {
            var concurrent = await concurrentContext.Suppliers.SingleAsync(item => item.Id == supplier.Id);
            concurrent.Name = "Изменённый поставщик";
            await concurrentContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => CreateService(context).DeleteFundAsync(
            fund.Id, new DeleteFundRequest("Закрытие", fund.Version), null, CancellationToken.None));

        await using var verification = database.CreateContext();
        var saved = await verification.Funds.SingleAsync(item => item.Id == fund.Id);
        Assert.False(saved.IsArchived);
        Assert.Equal(10m, saved.Balance);
        Assert.Equal(fund.Id, (await verification.Suppliers.SingleAsync(item => item.Id == supplier.Id)).ExpenseFundId);
        Assert.Empty(await verification.FundOperations.Where(item => item.FundId == fund.Id).ToListAsync());
        Assert.Empty(await verification.AuditEvents.Where(item => item.EntityId == fund.Id.ToString()).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task DeleteFund_CanceledRequestLeavesFundActive()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = await context.Funds.FirstAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(context).DeleteFundAsync(
            fund.Id, new DeleteFundRequest("Закрытие", fund.Version), null, cancellation.Token));
        Assert.False(fund.IsArchived);
    }

    private static FundService CreateService(GarageBalanceDbContext context) =>
        new(new EfFundRepository(context), new AuditEventWriter(context));
}
