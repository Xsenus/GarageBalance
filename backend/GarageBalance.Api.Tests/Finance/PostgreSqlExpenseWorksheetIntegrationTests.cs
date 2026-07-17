using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlExpenseWorksheetIntegrationTests
{
    [PostgreSqlFact]
    public async Task ExpenseWorksheet_AggregatesOpeningBalancesOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var supplierGroup = new SupplierGroup { Name = "Проверка коммунальных услуг PG" };
            var supplier = new Supplier { Name = "Водоканал", Group = supplierGroup };
            var waterType = new ExpenseType { Name = "Проверка водоснабжения PG", Code = "pg_water" };
            var salaryType = await seedContext.ExpenseTypes.SingleAsync(item => item.Code == "salary");
            var department = new StaffDepartment { Name = "Проверка бухгалтерии PG" };
            var staffMember = new StaffMember
            {
                FullName = "Петрова Ольга",
                Department = department,
                Rate = 100m,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
            };
            seedContext.AddRange(
                supplierGroup,
                supplier,
                waterType,
                department,
                staffMember,
                new SupplierAccrual
                {
                    Supplier = supplier,
                    ExpenseType = waterType,
                    AccountingMonth = new DateOnly(2026, 1, 1),
                    Amount = 100m,
                    Source = AccrualSources.Manual
                },
                CreateExpense(supplier, null, waterType, 40m),
                CreateExpense(null, staffMember, salaryType, 50m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(assertionContext).GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 2, 1)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(110m, result.Value!.OpeningBalanceTotal);
        Assert.Equal(60m, Assert.Single(result.Value.Rows, row => row.SupplierId.HasValue).OpeningBalance);
        Assert.Equal(50m, Assert.Single(result.Value.Rows, row => row.StaffMemberId.HasValue).OpeningBalance);
    }

    private static FinancialOperation CreateExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = new DateOnly(2026, 1, 20),
            AccountingMonth = new DateOnly(2026, 1, 1),
            Amount = amount,
            Supplier = supplier,
            StaffMember = staffMember,
            ExpenseType = expenseType
        };
}
