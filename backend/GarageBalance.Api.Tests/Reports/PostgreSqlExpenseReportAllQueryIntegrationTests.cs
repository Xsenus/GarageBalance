using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlExpenseReportAllQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task AllRowsLoadAccrualPaymentTotalsAndBoundedPageInOneCommand()
    {
        var month = new DateOnly(2043, 2, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var supplierName = $"Комплексный поставщик {suffix}";
        var staffName = $"Комплексный сотрудник {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        Guid staffId;
        Guid serviceExpenseTypeId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = $"All report {suffix}" };
            var supplier = new Supplier { Name = supplierName, StartingBalance = 100m, Group = group };
            var department = new StaffDepartment { Name = $"All department {suffix}" };
            var staff = new StaffMember
            {
                FullName = staffName,
                Rate = 200m,
                Department = department,
                CreatedAtUtc = new DateTimeOffset(2043, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2043, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var serviceExpenseType = new ExpenseType { Name = $"Обслуживание {suffix}", Code = $"all-service-{suffix}" };
            var salaryExpenseType = await seedContext.ExpenseTypes.SingleAsync(expenseType => expenseType.Code == "salary");
            seedContext.AddRange(group, supplier, department, staff, serviceExpenseType);
            seedContext.SupplierAccruals.AddRange(
                CreateAccrual(supplier, serviceExpenseType, month, 500m),
                CreateAccrual(supplier, serviceExpenseType, month, 999m, true),
                CreateAccrual(supplier, serviceExpenseType, month.AddMonths(-1), 300m));
            seedContext.FinancialOperations.AddRange(
                CreateSupplierPayment(supplier, serviceExpenseType, month.AddDays(4), 120m, "ALL-SUPPLIER"),
                CreateStaffPayment(staff, salaryExpenseType, month.AddDays(8), 80m, "ALL-STAFF"),
                CreateSupplierPayment(supplier, serviceExpenseType, month.AddDays(10), 777m, "ALL-CANCELED", true),
                CreateSupplierPayment(supplier, serviceExpenseType, month.AddMonths(-1), 50m, "ALL-OUTSIDE"));
            await seedContext.SaveChangesAsync();
            supplierId = supplier.Id;
            staffId = staff.Id;
            serviceExpenseTypeId = serviceExpenseType.Id;
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfExpenseReportQuery(context);

        var page = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            2,
            0,
            new ReportSort("difference", true),
            CancellationToken.None);

        Assert.Equal(5, page.RowCount);
        Assert.Equal(800m, page.AccrualTotal);
        Assert.Equal(200m, page.ExpenseTotal);
        Assert.Equal(2, page.Rows.Count);
        Assert.Collection(
            page.Rows,
            row =>
            {
                Assert.Equal("accruals", row.RowType);
                Assert.Equal(500m, row.AccrualAmount);
                Assert.Equal(supplierName, row.SupplierName);
            },
            row =>
            {
                Assert.Equal("accruals", row.RowType);
                Assert.Equal(200m, row.AccrualAmount);
                Assert.Equal(staffName, row.SupplierName);
            });
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_rows AS", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(accrual_amount), 0)", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(expense_amount), 0)", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM supplier_accruals"));
        Assert.Equal(2, CountOccurrences(pageCommand, "FROM financial_operations"));

        foreach (var descending in new[] { false, true })
        {
            foreach (var field in new[] { "date", "accountingMonth", "supplierName", "expenseTypeName", "accrualAmount", "expenseAmount", "difference", "documentNumber" })
            {
                capture.Commands.Clear();
                var sortedPage = await query.GetRowsAsync(
                    month,
                    month.AddMonths(1).AddDays(-1),
                    "all",
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    new HashSet<Guid>(),
                    null,
                    1,
                    1,
                    new ReportSort(field, descending),
                    CancellationToken.None);

                Assert.Equal(5, sortedPage.RowCount);
                Assert.Equal(800m, sortedPage.AccrualTotal);
                Assert.Equal(200m, sortedPage.ExpenseTotal);
                Assert.Single(sortedPage.Rows);
                Assert.Single(capture.Commands);
            }
        }

        capture.Commands.Clear();
        var supplierOnly = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid> { supplierId },
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            supplierName.ToUpperInvariant(),
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(3, supplierOnly.RowCount);
        Assert.Equal(600m, supplierOnly.AccrualTotal);
        Assert.Equal(120m, supplierOnly.ExpenseTotal);
        Assert.All(supplierOnly.Rows, row => Assert.Equal("supplier", row.CounterpartyKind));
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var staffOnly = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid> { staffId },
            new HashSet<Guid>(),
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(2, staffOnly.RowCount);
        Assert.Equal(200m, staffOnly.AccrualTotal);
        Assert.Equal(80m, staffOnly.ExpenseTotal);
        Assert.All(staffOnly.Rows, row => Assert.Equal("staff", row.CounterpartyKind));
        Assert.Single(capture.Commands);

        capture.Commands.Clear();
        var typeFiltered = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "all",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid> { serviceExpenseTypeId },
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(2, typeFiltered.RowCount);
        Assert.Equal(500m, typeFiltered.AccrualTotal);
        Assert.Equal(120m, typeFiltered.ExpenseTotal);
        Assert.DoesNotContain(typeFiltered.Rows, row => row.RowType == "starting_balance");
        Assert.Single(capture.Commands);
    }

    private static SupplierAccrual CreateAccrual(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly accountingMonth,
        decimal amount,
        bool isCanceled = false) =>
        new()
        {
            Supplier = supplier,
            ExpenseType = expenseType,
            AccountingMonth = accountingMonth,
            Amount = amount,
            Source = "expense_all_query_integration",
            IsCanceled = isCanceled
        };

    private static FinancialOperation CreateSupplierPayment(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        bool isCanceled = false) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Supplier = supplier,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(operationDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static FinancialOperation CreateStaffPayment(
        StaffMember staff,
        ExpenseType expenseType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            StaffMember = staff,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(operationDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
        };

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
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
