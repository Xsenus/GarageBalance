using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialReportPeriodIntegrationTests
{
    [PostgreSqlFact]
    public async Task SupplierPeriod_UsesFirstAndLastActiveMonthsOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = "Группа полного периода PG" };
            var supplier = new Supplier { Name = "Поставщик полного периода PG", Group = group };
            var expenseType = new ExpenseType { Name = "Услуга полного периода PG", Code = "pg_full_period" };
            supplierId = supplier.Id;
            seedContext.AddRange(
                group,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2023, 2, 1)),
                CreateAccrual(supplier, expenseType, new DateOnly(2022, 1, 1), isCanceled: true),
                CreateExpense(supplier, expenseType, new DateOnly(2027, 3, 1)),
                CreateExpense(supplier, expenseType, new DateOnly(2028, 4, 1), isCanceled: true));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(context).GetFinancialReportPeriodAsync(
            new FinancialReportPeriodRequest(null, supplierId, null),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2023, 2, 1), result.Value!.MonthFrom);
        Assert.Equal(new DateOnly(2027, 3, 1), result.Value.MonthTo);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        bool isCanceled = false) => new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = month,
            Amount = 100m,
            Source = AccrualSources.Manual,
            Comment = "Проверка полного периода",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateExpense(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly month,
        bool isCanceled = false) => new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = month,
            AccountingMonth = month,
            Amount = 100m,
            Supplier = supplier,
            ExpenseType = expenseType,
            IsCanceled = isCanceled
        };
}
