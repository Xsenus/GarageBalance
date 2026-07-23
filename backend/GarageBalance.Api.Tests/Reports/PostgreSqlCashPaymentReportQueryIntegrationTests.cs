using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlCashPaymentReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task CashPaymentPageUsesOneCommandAndPreservesTotalsSearchAndProjection()
    {
        var month = new DateOnly(2042, 7, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var firstSupplierName = $"Энергосбыт {suffix}";
        var electricityName = $"Электроэнергия {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var group = new SupplierGroup { Name = $"Cash report {suffix}" };
            var firstSupplier = new Supplier { Name = firstSupplierName, Group = group };
            var secondSupplier = new Supplier { Name = $"Ремонтная служба {suffix}", Group = group };
            var electricity = new ExpenseType { Name = electricityName };
            var repair = new ExpenseType { Name = $"Ремонт {suffix}" };
            seedContext.AddRange(group, firstSupplier, secondSupplier, electricity, repair);
            seedContext.FinancialOperations.AddRange(
                CreateExpense(firstSupplier, electricity, month.AddDays(4), 100m, "КО-100", "Оплата июля", expensePaymentType: ExpensePaymentTypes.WithoutReceipt),
                CreateExpense(secondSupplier, repair, month.AddDays(8), 50m, null, "Текущий ремонт"),
                CreateExpense(firstSupplier, electricity, month.AddDays(10), 999m, "CANCELED", "Отменено", true),
                CreateExpense(firstSupplier, electricity, month.AddMonths(-1), 200m, "OUTSIDE", "Другой месяц"),
                new FinancialOperation
                {
                    OperationKind = FinancialOperationKinds.Income,
                    OperationDate = month.AddDays(5),
                    AccountingMonth = month,
                    Amount = 300m,
                    DocumentNumber = "INCOME",
                    CreatedAtUtc = new DateTimeOffset(month.AddDays(5).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
                });
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfCashMovementReportQuery(context);

        var page = await query.GetCashPaymentsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);

        Assert.Equal(2, page.RowCount);
        Assert.Equal(150m, page.Total);
        var operation = Assert.Single(page.Operations);
        Assert.Equal(100m, operation.Amount);
        Assert.Equal(firstSupplierName, operation.SupplierName);
        Assert.Equal(electricityName, operation.ExpenseTypeName);
        Assert.False(operation.HasReceipt);
        Assert.Equal("КО-100", operation.DocumentNumber);
        Assert.Equal("Оплата июля", operation.Comment);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM financial_operations"));
        Assert.Contains("FROM page_rows", pageCommand, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", pageCommand, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", pageCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", pageCommand, StringComparison.Ordinal);

        capture.Commands.Clear();
        var searchResult = await query.GetCashPaymentsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "ЭНЕРГО",
            0,
            25,
            new ReportSort("operationDate", false),
            CancellationToken.None);

        Assert.Equal(1, searchResult.RowCount);
        Assert.Equal(100m, searchResult.Total);
        Assert.Equal(firstSupplierName, Assert.Single(searchResult.Operations).SupplierName);
        var searchCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(searchCommand, "FROM financial_operations"));
        Assert.Contains("LOWER(supplier.\"Name\")", searchCommand, StringComparison.Ordinal);
    }

    private static FinancialOperation CreateExpense(
        Supplier supplier,
        ExpenseType expenseType,
        DateOnly operationDate,
        decimal amount,
        string? documentNumber,
        string comment,
        bool isCanceled = false,
        string expensePaymentType = ExpensePaymentTypes.WithReceipt) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Expense,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            ExpensePaymentType = expensePaymentType,
            Supplier = supplier,
            ExpenseType = expenseType,
            DocumentNumber = documentNumber,
            Comment = comment,
            IsCanceled = isCanceled,
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
