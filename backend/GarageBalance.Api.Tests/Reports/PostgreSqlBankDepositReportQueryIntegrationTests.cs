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
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.CashBankTransfers.AddRange(
                CreateTransfer(month.AddDays(4), 100m, $"Сдача резерва {suffix}"),
                CreateTransfer(month.AddDays(8), 50m, "Сдача текущих средств"),
                CreateTransfer(month.AddDays(10), 999m, "Отменено", isCanceled: true),
                CreateTransfer(month.AddMonths(-1), 200m, "Другой месяц"));
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
        Assert.Equal($"Сдача резерва {suffix}", operation.Comment);
        Assert.Equal(month.AddDays(4), operation.TransferDate);
        var pageCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(pageCommand, "FROM cash_bank_transfers"));
        Assert.Contains("FROM page_rows", pageCommand, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::int", pageCommand, StringComparison.Ordinal);
        Assert.Contains("OFFSET @offset", pageCommand, StringComparison.Ordinal);
        Assert.Contains("LIMIT @limit", pageCommand, StringComparison.Ordinal);

        capture.Commands.Clear();
        var searchResult = await query.GetBankDepositsAsync(
            month,
            month.AddMonths(1).AddDays(-1),
            suffix.ToUpperInvariant(),
            0,
            25,
            new ReportSort("date", false),
            CancellationToken.None);

        Assert.Equal(1, searchResult.RowCount);
        Assert.Equal(100m, searchResult.Total);
        Assert.Equal($"Сдача резерва {suffix}", Assert.Single(searchResult.Operations).Comment);
        var searchCommand = Assert.Single(capture.Commands);
        Assert.Equal(1, CountOccurrences(searchCommand, "FROM cash_bank_transfers"));
        Assert.Contains("LOWER(COALESCE(transfer.\"Comment\", ''))", searchCommand, StringComparison.Ordinal);
    }

    private static CashBankTransfer CreateTransfer(
        DateOnly date,
        decimal amount,
        string comment,
        bool isCanceled = false) =>
        new()
        {
            TransferDate = date,
            Amount = amount,
            Comment = comment,
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
