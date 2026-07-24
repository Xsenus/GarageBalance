using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlIncomeReportPaymentQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task PaymentPageLoadsTotalsPageAndSequentialDebtInTwoCommands()
    {
        var month = new DateOnly(2042, 10, 1);
        var suffix = Guid.NewGuid().ToString("N");
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = $"INCOME-{suffix}", StartingBalance = 100m };
            var incomeType = new IncomeType { Name = $"Income payment {suffix}" };
            seedContext.AddRange(garage, incomeType);
            seedContext.Accruals.Add(new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = month,
                DueDate = month.AddMonths(1).AddDays(-1),
                OverdueFromDate = month.AddMonths(1),
                Amount = 500m,
                Source = "income_payment_query_integration"
            });
            seedContext.FinancialOperations.AddRange(
                CreatePayment(garage, incomeType, month.AddDays(4), 100m, "PKO-FIRST"),
                CreatePayment(garage, incomeType, month.AddDays(8), 200m, "PKO-SECOND"));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfIncomeReportQuery(context).GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(2, result.RowCount);
        Assert.Equal(0m, result.AccrualTotal);
        Assert.Equal(300m, result.IncomeTotal);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(500m, result.Rows[0].DebtAfterPayment);
        Assert.Equal(300m, result.Rows[1].DebtAfterPayment);
        Assert.Equal("PKO-FIRST", result.Rows[0].DocumentNumber);
        Assert.Equal("PKO-SECOND", result.Rows[1].DocumentNumber);
        Assert.Equal(2, capture.Commands.Count);
        Assert.Equal(2, capture.Commands.Count(command => command.Contains("financial_operations", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("WITH filtered_rows AS", capture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COALESCE(SUM(income_amount), 0)", capture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(capture.Commands, command =>
            command.Contains("WHERE f.\"Id\" = ANY", StringComparison.OrdinalIgnoreCase) ||
            command.Contains("WHERE \"f\".\"Id\" = ANY", StringComparison.OrdinalIgnoreCase));
    }

    [PostgreSqlFact]
    public async Task GroupedPaymentPageCombinesReceiptBatchAndKeepsStandalonePayment()
    {
        var month = new DateOnly(2043, 2, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var receiptBatchId = Guid.NewGuid();
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = $"GROUPED-{suffix}", StartingBalance = 100m };
            var membership = new IncomeType { Name = $"Membership {suffix}" };
            var electricity = new IncomeType { Name = $"Electricity {suffix}" };
            seedContext.AddRange(garage, membership, electricity);
            seedContext.Accruals.Add(new Accrual
            {
                Garage = garage,
                IncomeType = membership,
                AccountingMonth = month,
                DueDate = month.AddMonths(1).AddDays(-1),
                OverdueFromDate = month.AddMonths(1),
                Amount = 500m,
                Source = "grouped_income_payment_query_integration"
            });
            seedContext.FinancialOperations.AddRange(
                CreatePayment(garage, membership, month.AddDays(4), 100m, "PKO-BATCH-1", receiptBatchId, 10),
                CreatePayment(garage, electricity, month.AddDays(4), 200m, "PKO-BATCH-2", receiptBatchId, 11),
                CreatePayment(garage, membership, month.AddDays(8), 50m, "PKO-STANDALONE"));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var result = await new EfIncomeReportQuery(context).GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            25,
            0,
            new ReportSort("date", false),
            true,
            CancellationToken.None);

        Assert.Equal(2, result.RowCount);
        Assert.Equal(350m, result.IncomeTotal);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(300m, result.Rows[0].IncomeAmount);
        Assert.Equal(300m, result.Rows[0].DebtAfterPayment);
        Assert.Contains($"Membership {suffix}", result.Rows[0].IncomeTypeName, StringComparison.Ordinal);
        Assert.Contains($"Electricity {suffix}", result.Rows[0].IncomeTypeName, StringComparison.Ordinal);
        Assert.Equal("PKO-BATCH-1, PKO-BATCH-2", result.Rows[0].DocumentNumber);
        Assert.Equal(50m, result.Rows[1].IncomeAmount);
        Assert.Equal(250m, result.Rows[1].DebtAfterPayment);
        Assert.Equal("PKO-STANDALONE", result.Rows[1].DocumentNumber);
    }

    [PostgreSqlFact]
    public async Task GroupedPaymentControlTotalIsCalculatedOnceFromSourceOperationsOnEveryPage()
    {
        var month = new DateOnly(2043, 3, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var receiptBatchId = Guid.NewGuid();
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage { Number = $"CONTROL-TOTAL-{suffix}" };
            var membership = new IncomeType { Name = $"Control membership {suffix}" };
            var electricity = new IncomeType { Name = $"Control electricity {suffix}" };
            seedContext.AddRange(garage, membership, electricity);
            seedContext.FinancialOperations.AddRange(
                CreatePayment(garage, membership, month.AddDays(4), 100000m, "PKO-CONTROL-1", receiptBatchId, 10),
                CreatePayment(garage, electricity, month.AddDays(4), 107436m, "PKO-CONTROL-2", receiptBatchId, 11));
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var query = new EfIncomeReportQuery(context);
        var firstPage = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            0,
            new ReportSort("date", false),
            true,
            CancellationToken.None);
        var emptySecondPage = await query.GetRowsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "payments",
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            null,
            1,
            1,
            new ReportSort("date", false),
            true,
            CancellationToken.None);

        Assert.Equal(1, firstPage.RowCount);
        Assert.Single(firstPage.Rows);
        Assert.Equal(207436m, firstPage.Rows[0].IncomeAmount);
        Assert.Equal(207436m, firstPage.IncomeTotal);
        Assert.NotEqual(414872m, firstPage.IncomeTotal);
        Assert.Equal(1, emptySecondPage.RowCount);
        Assert.Empty(emptySecondPage.Rows);
        Assert.Equal(firstPage.IncomeTotal, emptySecondPage.IncomeTotal);
    }

    private static FinancialOperation CreatePayment(
        Garage garage,
        IncomeType incomeType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber,
        Guid? receiptBatchId = null,
        int hour = 10) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            ReceiptBatchId = receiptBatchId,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(operationDate.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero)
        };

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
