using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlBankDepositReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task BankDepositPageUsesOneCommandAndPreservesTotalsSearchAndProjection()
    {
        var month = new DateOnly(2042, 8, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var firstFundName = $"Резервный фонд {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var firstFund = new Fund
            {
                Name = firstFundName,
                NormalizedName = $"РЕЗЕРВНЫЙ ФОНД {suffix.ToUpperInvariant()}",
                SortOrder = 901
            };
            var secondFund = new Fund
            {
                Name = $"Текущий фонд {suffix}",
                NormalizedName = $"ТЕКУЩИЙ ФОНД {suffix.ToUpperInvariant()}",
                SortOrder = 902
            };
            seedContext.AddRange(firstFund, secondFund);
            seedContext.FundOperations.AddRange(
                CreateOperation(firstFund, month.AddDays(4), 100m, "Сдача резерва", true),
                CreateOperation(secondFund, month.AddDays(8), 50m, "Сдача текущих средств", true),
                CreateOperation(firstFund, month.AddDays(10), 999m, "Отменено", true, isCanceled: true),
                CreateOperation(firstFund, month.AddDays(12), 300m, "Не банковская операция", false),
                CreateOperation(firstFund, month.AddMonths(-1), 200m, "Другой месяц", true),
                CreateOperation(firstFund, month.AddDays(14), 400m, "Списание", true, FundOperationKinds.Withdraw));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfCashMovementReportQuery(context);

        var page = await query.GetBankDepositsAsync(
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
        Assert.Equal(firstFundName, operation.FundName);
        Assert.Equal("Сдача резерва", operation.Reason);
        Assert.Equal(month.AddDays(4), DateOnly.FromDateTime(operation.CreatedAtUtc.UtcDateTime));
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM fund_operations"));
        Assert.Contains("FROM page_rows", pageCommand, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", pageCommand, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", pageCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", pageCommand, StringComparison.Ordinal);

        capture.Commands.Clear();
        var searchResult = await query.GetBankDepositsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "РЕЗЕРВНЫЙ",
            0,
            25,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, searchResult.RowCount);
        Assert.Equal(100m, searchResult.Total);
        Assert.Equal(firstFundName, Assert.Single(searchResult.Operations).FundName);
        var searchCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(searchCommand, "FROM fund_operations"));
        Assert.Contains("LOWER(fund.\"Name\")", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LOWER(operation.\"Reason\")", searchCommand, StringComparison.Ordinal);
    }

    private static FundOperation CreateOperation(
        Fund fund,
        DateOnly date,
        decimal amount,
        string reason,
        bool isCashToBankTransfer,
        string operationKind = FundOperationKinds.Deposit,
        bool isCanceled = false) =>
        new()
        {
            Fund = fund,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = 0m,
            BalanceAfter = amount,
            Reason = reason,
            IsCashToBankTransfer = isCashToBankTransfer,
            IsCanceled = isCanceled,
            CreatedAtUtc = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero)
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
