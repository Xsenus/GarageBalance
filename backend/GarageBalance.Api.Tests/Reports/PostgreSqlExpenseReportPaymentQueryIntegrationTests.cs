using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlExpenseReportPaymentQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task PaymentPageLoadsSupplierAndStaffTotalsAndPageInOneCommand()
    {
        var month = new DateOnly(2042, 11, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var supplierName = $"Энергосервис {suffix}";
        var staffName = $"Сотрудник {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid supplierId;
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = $"Expense report {suffix}" };
            var supplier = new Supplier { Name = supplierName, Group = group };
            var department = new StaffDepartment { Name = $"Department {suffix}" };
            var staff = new StaffMember
            {
                FullName = staffName,
                Rate = 10m,
                Department = department,
                CreatedAtUtc = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2042, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var expenseType = new ExpenseType { Name = $"Эксплуатация {suffix}" };
            seedContext.AddRange(group, supplier, department, staff, expenseType);
            seedContext.FinancialOperations.AddRange(
                CreateSupplierPayment(supplier, expenseType, month.AddDays(4), 100m, "КО-SUPPLIER"),
                CreateStaffPayment(staff, expenseType, month.AddDays(8), 200m, "КО-STAFF"),
                CreateSupplierPayment(supplier, expenseType, month.AddDays(10), 999m, "CANCELED", true),
                CreateSupplierPayment(supplier, expenseType, month.AddMonths(-1), 300m, "OUTSIDE"));
            await seedContext.SaveChangesAsync();
            supplierId = supplier.Id;
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
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            0,
            new ReportSort("expenseAmount", true),
            CancellationToken.None);

        Assert.Equal(2, page.RowCount);
        Assert.Equal(0m, page.AccrualTotal);
        Assert.Equal(300m, page.ExpenseTotal);
        var staffRow = Assert.Single(page.Rows);
        Assert.Equal(staffName, staffRow.SupplierName);
        Assert.Equal("staff", staffRow.CounterpartyKind);
        Assert.Equal(200m, staffRow.ExpenseAmount);
        Assert.Equal("КО-STAFF", staffRow.DocumentNumber);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Contains("WITH filtered_rows AS", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(expense_amount), 0)", pageCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, CountOccurrences(pageCommand, "FROM financial_operations"));

        capture.Commands.Clear();
        var supplierOnly = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid> { supplierId },
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            "ЭНЕРГО",
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, supplierOnly.RowCount);
        Assert.Equal(100m, supplierOnly.ExpenseTotal);
        var supplierRow = Assert.Single(supplierOnly.Rows);
        Assert.Equal(supplierName, supplierRow.SupplierName);
        Assert.Equal("supplier", supplierRow.CounterpartyKind);
        Assert.Single(capture.Commands);
    }

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
