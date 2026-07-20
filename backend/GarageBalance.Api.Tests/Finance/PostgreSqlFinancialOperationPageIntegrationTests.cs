using System.Data.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinancialOperationPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task OperationPageLoadsCountMixedRowsAndRelatedNamesInOneCommandForEveryPageShape()
    {
        var builder = new AccountingTestDataBuilder();
        var owner = builder.BuildOwner("Петров", "Пётр", "Петрович");
        var garage = builder.BuildGarage(owner, "Target-2048");
        var ordinaryGarage = builder.BuildGarage(number: "Обычный-2048");
        var incomeType = builder.BuildIncomeType("Взнос страницы операций 2048", "operation_page_income_2048");
        var supplierGroup = builder.BuildSupplierGroup("Группа страницы операций 2048");
        var supplier = builder.BuildSupplier(supplierGroup, "Target поставщик 2048");
        var expenseType = builder.BuildExpenseType("Расход страницы операций 2048", "operation_page_expense_2048");
        var department = new StaffDepartment { Name = "Отдел страницы операций 2048" };
        var staffMember = new StaffMember
        {
            FullName = "Target сотрудник 2048",
            Rate = 500m,
            Department = department,
            DepartmentId = department.Id
        };
        var expectedCreatedAt = new DateTimeOffset(2048, 3, 4, 8, 15, 0, TimeSpan.Zero);
        var expectedUpdatedAt = expectedCreatedAt.AddMinutes(30);
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.FinancialOperations.AddRange(
                CreateIncome(
                    garage,
                    incomeType,
                    new DateOnly(2048, 4, 10),
                    1000m,
                    "IN-04",
                    "Поступление по номеру гаража"),
                CreateSupplierExpense(
                    supplier,
                    expenseType,
                    new DateOnly(2048, 3, 10),
                    725.50m,
                    "OUT-03",
                    "Выплата поставщику",
                    expectedCreatedAt,
                    expectedUpdatedAt),
                CreateStaffExpense(
                    staffMember,
                    expenseType,
                    new DateOnly(2048, 2, 10),
                    500m,
                    "OUT-02",
                    "Выплата сотруднику"),
                CreateIncome(
                    garage,
                    incomeType,
                    new DateOnly(2048, 1, 10),
                    300m,
                    "IN-01",
                    "Target комментарий 2048"),
                CreateIncome(
                    garage,
                    incomeType,
                    new DateOnly(2047, 12, 10),
                    200m,
                    "IN-OLD",
                    "Target вне периода"),
                CreateIncome(
                    garage,
                    incomeType,
                    new DateOnly(2048, 5, 10),
                    100m,
                    "IN-CANCEL",
                    "Target отменённая операция",
                    isCanceled: true),
                CreateIncome(
                    ordinaryGarage,
                    incomeType,
                    new DateOnly(2048, 3, 12),
                    150m,
                    "IN-PLAIN",
                    "Обычная операция"));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var repository = new EfFinancialOperationRepository(context);

        var page = await repository.GetPageAsync(
            new DateOnly(2048, 1, 1),
            new DateOnly(2048, 4, 30),
            null,
            "target",
            null,
            null,
            null,
            1,
            3,
            CancellationToken.None);

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(
            [new DateOnly(2048, 3, 10), new DateOnly(2048, 2, 10), new DateOnly(2048, 1, 10)],
            page.Items.Select(operation => operation.OperationDate));

        var supplierExpense = page.Items[0];
        Assert.Equal(FinancialOperationKinds.Expense, supplierExpense.OperationKind);
        Assert.Equal(supplier.Id, supplierExpense.SupplierId);
        Assert.Equal("Target поставщик 2048", supplierExpense.Supplier!.Name);
        Assert.Equal(expenseType.Id, supplierExpense.ExpenseTypeId);
        Assert.Equal("Расход страницы операций 2048", supplierExpense.ExpenseType!.Name);
        Assert.Null(supplierExpense.GarageId);
        Assert.Null(supplierExpense.StaffMemberId);
        Assert.Equal(725.50m, supplierExpense.Amount);
        Assert.Equal("OUT-03", supplierExpense.DocumentNumber);
        Assert.Equal("Выплата поставщику", supplierExpense.Comment);
        Assert.False(supplierExpense.IsCanceled);
        Assert.Equal(expectedCreatedAt, supplierExpense.CreatedAtUtc);
        Assert.Equal(expectedUpdatedAt, supplierExpense.UpdatedAtUtc);

        var staffExpense = page.Items[1];
        Assert.Equal(staffMember.Id, staffExpense.StaffMemberId);
        Assert.Equal("Target сотрудник 2048", staffExpense.StaffMember!.FullName);
        Assert.Equal(department.Id, staffExpense.StaffMember.DepartmentId);
        Assert.Equal("Отдел страницы операций 2048", staffExpense.StaffMember.Department.Name);
        Assert.Equal(expenseType.Id, staffExpense.ExpenseTypeId);

        var income = page.Items[2];
        Assert.Equal(FinancialOperationKinds.Income, income.OperationKind);
        Assert.Equal(garage.Id, income.GarageId);
        Assert.Equal("Target-2048", income.Garage!.Number);
        Assert.Equal(owner.FullName, income.Garage.Owner!.FullName);
        Assert.Equal(incomeType.Id, income.IncomeTypeId);
        Assert.Equal("Взнос страницы операций 2048", income.IncomeType!.Name);
        Assert.Null(income.SupplierId);
        Assert.Null(income.ExpenseTypeId);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var expenses = await repository.GetPageAsync(
            new DateOnly(2048, 1, 1),
            new DateOnly(2048, 4, 30),
            FinancialOperationKinds.Expense,
            "target",
            null,
            null,
            null,
            0,
            10,
            CancellationToken.None);

        Assert.Equal(2, expenses.TotalCount);
        Assert.Equal([supplier.Id, null], expenses.Items.Select(operation => operation.SupplierId));
        Assert.Equal([null, staffMember.Id], expenses.Items.Select(operation => operation.StaffMemberId));
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var garagePage = await repository.GetPageAsync(
            new DateOnly(2048, 1, 1),
            new DateOnly(2048, 4, 30),
            FinancialOperationKinds.Income,
            "target",
            garage.Id,
            null,
            null,
            0,
            10,
            CancellationToken.None);

        Assert.Equal(2, garagePage.TotalCount);
        Assert.All(garagePage.Items, operation => Assert.Equal(garage.Id, operation.GarageId));
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var beyondEnd = await repository.GetPageAsync(
            new DateOnly(2048, 1, 1),
            new DateOnly(2048, 4, 30),
            null,
            "target",
            null,
            null,
            null,
            20,
            5,
            CancellationToken.None);

        Assert.Equal(4, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Items);
        AssertSingleCombinedCommand(capture);

        capture.Commands.Clear();
        var empty = await repository.GetPageAsync(null, null, null, "missing", null, null, null, 0, 5, CancellationToken.None);

        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Items);
        AssertSingleCombinedCommand(capture);
    }

    private static FinancialOperation CreateIncome(
        Garage garage,
        IncomeType incomeType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        string comment,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            DocumentNumber = documentNumber,
            Comment = comment,
            Garage = garage,
            IncomeType = incomeType,
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateSupplierExpense(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        string comment,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            DocumentNumber = documentNumber,
            Comment = comment,
            Supplier = supplier,
            ExpenseType = expenseType,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        };

    private static FinancialOperation CreateStaffExpense(
        StaffMember staffMember,
        ExpenseType expenseType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        string comment) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            DocumentNumber = documentNumber,
            Comment = comment,
            StaffMember = staffMember,
            ExpenseType = expenseType
        };

    private static void AssertSingleCombinedCommand(ReaderCommandCapture capture)
    {
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("financial_operations", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garages", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owners", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("income_types", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("suppliers", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("staff_members", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("staff_departments", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expense_types", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReaderCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
