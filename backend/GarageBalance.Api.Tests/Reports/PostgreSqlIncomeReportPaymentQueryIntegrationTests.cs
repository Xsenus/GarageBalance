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

    private static FinancialOperation CreatePayment(
        Garage garage,
        IncomeType incomeType,
        DateOnly operationDate,
        decimal amount,
        string documentNumber) =>
        new()
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = operationDate,
            AccountingMonth = new DateOnly(operationDate.Year, operationDate.Month, 1),
            Amount = amount,
            Garage = garage,
            IncomeType = incomeType,
            DocumentNumber = documentNumber,
            CreatedAtUtc = new DateTimeOffset(operationDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
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
