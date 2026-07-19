using System.Data.Common;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Reports;

public sealed class PostgreSqlFundChangeReportQueryIntegrationTests
{
    [PostgreSqlFact]
    public async Task FundChangePageUsesOneCommandAndPreservesTotalsSearchActorAndProjection()
    {
        var month = new DateOnly(2042, 9, 1);
        var suffix = Guid.NewGuid().ToString("N");
        var fundName = $"Резервный фонд {suffix}";
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            var actor = new AppUser
            {
                Email = $"fund-change-{suffix}@example.test",
                NormalizedEmail = $"FUND-CHANGE-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
                DisplayName = "Оператор фондов",
                PasswordHash = "integration-test-hash"
            };
            var fund = new Fund
            {
                Name = fundName,
                NormalizedName = $"РЕЗЕРВНЫЙ ФОНД {suffix.ToUpperInvariant()}",
                SortOrder = 903
            };
            seedContext.AddRange(actor, fund);
            seedContext.FundOperations.AddRange(
                CreateOperation(fund, actor.Id, month.AddDays(4), 100m, 0m, 100m, FundOperationKinds.Deposit, "Пополнение резерва"),
                CreateOperation(fund, null, month.AddDays(8), 40m, 100m, 60m, FundOperationKinds.Withdraw, "Оплата ремонта"),
                CreateOperation(fund, actor.Id, month.AddDays(10), 999m, 60m, 1059m, FundOperationKinds.Deposit, "Отменено", true),
                CreateOperation(fund, actor.Id, month.AddMonths(-1), 200m, 0m, 200m, FundOperationKinds.Deposit, "Другой месяц"));
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        var query = new EfFundChangeReportQuery(context);

        var page = await query.GetFundChangesAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            null,
            0,
            1,
            new ReportSort("amount", true),
            CancellationToken.None);

        Assert.Equal(2, page.RowCount);
        Assert.Equal(100m, page.DepositTotal);
        Assert.Equal(40m, page.WithdrawalTotal);
        var row = Assert.Single(page.Rows);
        Assert.Equal(100m, row.Amount);
        Assert.Equal(fundName, row.FundName);
        Assert.Equal("Оператор фондов", row.ActorDisplayName);
        Assert.Equal("Пополнение резерва", row.Reason);
        Assert.Equal(0m, row.BalanceBefore);
        Assert.Equal(100m, row.BalanceAfter);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM fund_operations"));
        Assert.Contains("LEFT JOIN app_users actor", pageCommand, StringComparison.Ordinal);
        Assert.Contains("FROM page_rows", pageCommand, StringComparison.Ordinal);
        Assert.Contains("SUM(amount) FILTER", pageCommand, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", pageCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", pageCommand, StringComparison.Ordinal);

        capture.Commands.Clear();
        var searchResult = await query.GetFundChangesAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            "РЕЗЕРВНЫЙ",
            0,
            25,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(2, searchResult.RowCount);
        Assert.Equal(100m, searchResult.DepositTotal);
        Assert.Equal(40m, searchResult.WithdrawalTotal);
        Assert.Equal(2, searchResult.Rows.Count);
        Assert.Contains(searchResult.Rows, operation => operation.ActorDisplayName is null && operation.OperationKind == FundOperationKinds.Withdraw);
        var searchCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(searchCommand, "FROM fund_operations"));
        Assert.Contains("LOWER(fund.\"Name\")", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LOWER(operation.\"OperationKind\")", searchCommand, StringComparison.Ordinal);
        Assert.Contains("LOWER(operation.\"Reason\")", searchCommand, StringComparison.Ordinal);
    }

    private static FundOperation CreateOperation(
        Fund fund,
        Guid? actorUserId,
        DateOnly date,
        decimal amount,
        decimal balanceBefore,
        decimal balanceAfter,
        string operationKind,
        string reason,
        bool isCanceled = false) =>
        new()
        {
            Fund = fund,
            ActorUserId = actorUserId,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Reason = reason,
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
