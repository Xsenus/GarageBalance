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
                new SupplierAccrual
                {
                    Supplier = supplier,
                    ExpenseType = waterType,
                    AccountingMonth = new DateOnly(2026, 2, 1),
                    Amount = 30m,
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
        Assert.Equal(110m, result.Value.OpeningDebtTotal);
        Assert.Equal(0m, result.Value.OpeningAdvanceTotal);
        var supplierRow = Assert.Single(result.Value.Rows, row => row.SupplierId.HasValue);
        Assert.Equal(60m, supplierRow.OpeningBalance);
        Assert.Equal(60m, supplierRow.OpeningDebt);
        Assert.Equal(90m, supplierRow.ClosingDebt);
        var staffRow = Assert.Single(result.Value.Rows, row => row.StaffMemberId.HasValue);
        Assert.Equal(50m, staffRow.OpeningBalance);
        Assert.Equal(50m, staffRow.OpeningDebt);
        Assert.Equal(150m, staffRow.ClosingDebt);
        Assert.Equal(240m, result.Value.ClosingDebtTotal);
        Assert.Equal(0m, result.Value.ClosingAdvanceTotal);
    }

    [PostgreSqlFact]
    public async Task ExpenseWorksheet_CarriesDebtAndAdvanceAcrossMonthsWithoutPersistingTransfers()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid expenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var supplierGroup = new SupplierGroup { Name = "Перенос выплат PG" };
            var supplier = new Supplier { Name = "Поставщик переноса PG", Group = supplierGroup };
            var expenseType = new ExpenseType { Name = "Услуга переноса PG", Code = "pg_carry_service" };
            supplierId = supplier.Id;
            expenseTypeId = expenseType.Id;
            seedContext.AddRange(
                supplierGroup,
                supplier,
                expenseType,
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 1, 1), 100m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 2, 1), 200m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 3, 1), 100m),
                CreateAccrual(supplier, expenseType, new DateOnly(2026, 4, 1), 80m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 1, 1), 100m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 2, 1), 50m),
                CreateExpense(supplier, null, expenseType, new DateOnly(2026, 3, 1), 300m));
            await seedContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(assertionContext);
        var february = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 2, 1)), CancellationToken.None);
        var march = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 3, 1)), CancellationToken.None);
        var april = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);
        var repeatedApril = await service.GetExpenseWorksheetAsync(
            new ExpenseWorksheetRequest(new DateOnly(2026, 4, 1)), CancellationToken.None);

        AssertExpenseCarry(FindRow(february.Value!, supplierId, expenseTypeId), 0m, 0m, 150m, 0m);
        AssertExpenseCarry(FindRow(march.Value!, supplierId, expenseTypeId), 150m, 0m, 0m, 50m);
        AssertExpenseCarry(FindRow(april.Value!, supplierId, expenseTypeId), 0m, 50m, 30m, 0m);
        AssertExpenseCarry(FindRow(repeatedApril.Value!, supplierId, expenseTypeId), 0m, 50m, 30m, 0m);
        Assert.Equal(4, await assertionContext.SupplierAccruals.CountAsync());
        Assert.Equal(3, await assertionContext.FinancialOperations.CountAsync());
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = AccrualSources.Manual
        };

    private static FinancialOperation CreateExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        decimal amount) => CreateExpense(
            supplier,
            staffMember,
            expenseType,
            new DateOnly(2026, 1, 1),
            amount);

    private static FinancialOperation CreateExpense(
        Supplier? supplier,
        StaffMember? staffMember,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = accountingMonth.AddDays(19),
            AccountingMonth = accountingMonth,
            Amount = amount,
            Supplier = supplier,
            StaffMember = staffMember,
            ExpenseType = expenseType
        };

    private static ExpenseWorksheetRowDto FindRow(ExpenseWorksheetDto worksheet, Guid supplierId, Guid expenseTypeId) =>
        Assert.Single(worksheet.Rows, row => row.SupplierId == supplierId && row.ExpenseTypeId == expenseTypeId);

    private static void AssertExpenseCarry(
        ExpenseWorksheetRowDto row,
        decimal openingDebt,
        decimal openingAdvance,
        decimal closingDebt,
        decimal closingAdvance)
    {
        Assert.Equal(openingDebt, row.OpeningDebt);
        Assert.Equal(openingAdvance, row.OpeningAdvance);
        Assert.Equal(closingDebt, row.ClosingDebt);
        Assert.Equal(closingAdvance, row.ClosingAdvance);
    }
}
