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
    public async Task FundFilter_AppliesBeforeTotalsSearchAndPagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2043, 2, 1);
        var first = new Fund { Name = "Первый", NormalizedName = Guid.NewGuid().ToString(), SortOrder = 901 };
        var second = new Fund { Name = "Второй", NormalizedName = Guid.NewGuid().ToString(), SortOrder = 902 };
        context.AddRange(first, second);
        context.FundOperations.AddRange(
            CreateOperation(first, null, month, 100m, 0m, 100m, FundOperationKinds.Deposit, "Общий комментарий"),
            CreateOperation(first, null, month.AddDays(1), 30m, 100m, 70m, FundOperationKinds.Withdraw, "Общий комментарий"),
            CreateOperation(second, null, month, 900m, 0m, 900m, FundOperationKinds.Deposit, "Общий комментарий"),
            CreateOperation(first, null, month, 800m, 0m, 800m, FundOperationKinds.Deposit, "Отмена", true),
            CreateOperation(first, null, month.AddMonths(-1), 700m, 0m, 700m, FundOperationKinds.Deposit, "Прошлый месяц"));
        await context.SaveChangesAsync();
        var query = new EfFundChangeReportQuery(context);

        var page = await query.GetFundChangesAsync(month, month.AddMonths(1).AddDays(-1), "Общий", 1, 1,
            new ReportSort("date", false), CancellationToken.None, [first.Id, first.Id]);
        Assert.Equal(2, page.RowCount);
        Assert.Equal(100m, page.DepositTotal);
        Assert.Equal(30m, page.WithdrawalTotal);
        Assert.Equal(first.Id, Assert.Single(page.Rows).FundId);
        Assert.Equal(30m, page.Rows[0].Amount);

        foreach (var ids in new Guid[][] { [], [first.Id, second.Id] })
        {
            var all = await query.GetFundChangesAsync(month, month.AddMonths(1).AddDays(-1), null, 0, 25,
                new ReportSort("date", false), CancellationToken.None, ids);
            Assert.Equal(3, all.RowCount);
            Assert.Equal(1000m, all.DepositTotal);
            Assert.Equal(30m, all.WithdrawalTotal);
        }

        var missing = await query.GetFundChangesAsync(month, month.AddMonths(1).AddDays(-1), null, 0, 25,
            new ReportSort("date", false), CancellationToken.None, [Guid.NewGuid()]);
        Assert.Empty(missing.Rows);
        Assert.Equal(0, missing.RowCount);
        Assert.Equal(0m, missing.DepositTotal);
        Assert.Equal(0m, missing.WithdrawalTotal);
    }

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
        Assert.Contains("fund.\"Name\" ILIKE @search COLLATE \"und-x-icu\" ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("operation.\"OperationKind\" ILIKE @search COLLATE \"und-x-icu\" ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.Contains("operation.\"Reason\" ILIKE @search COLLATE \"und-x-icu\" ESCAPE '\\'", searchCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("LOWER(", searchCommand, StringComparison.Ordinal);
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
