using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlSupplierOpeningBalanceIntegrationTests
{
    [PostgreSqlFact]
    public async Task OpeningBalance_AggregatesFullActiveHistoryBeforePeriodOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = "Группа входящего остатка PG" };
            var supplier = new Supplier { Name = "Поставщик входящего остатка PG", Group = group, StartingBalance = 250m };
            var expenseType = new ExpenseType { Name = "Услуга входящего остатка PG", Code = "pg_supplier_opening" };
            supplierId = supplier.Id;
            seedContext.AddRange(
                group,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 5, 1), 500m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 4, 1), 70m, isCanceled: true),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 6, 1), 1000m),
                CreateExpense(supplier, expenseType, new DateOnly(2026, 5, 1), 100m),
                CreateExpense(supplier, expenseType, new DateOnly(2026, 4, 1), 30m, isCanceled: true),
                CreateExpense(supplier, expenseType, new DateOnly(2026, 6, 1), 600m));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(context).GetSupplierOpeningBalanceAsync(
            supplierId,
            new SupplierOpeningBalanceRequest(new DateOnly(2026, 6, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(250m, result.Value!.StartingBalance);
        Assert.Equal(500m, result.Value.PriorAccrualTotal);
        Assert.Equal(100m, result.Value.PriorPaymentTotal);
        Assert.Equal(650m, result.Value.OpeningBalance);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        decimal amount,
        bool isCanceled = false) => new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = month,
            Amount = amount,
            Source = AccrualSources.Manual,
            Comment = "Проверка входящего остатка",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateExpense(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        decimal amount,
        bool isCanceled = false) => new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = month,
            AccountingMonth = month,
            Amount = amount,
            Supplier = supplier,
            ExpenseType = expenseType,
            IsCanceled = isCanceled
        };
}
