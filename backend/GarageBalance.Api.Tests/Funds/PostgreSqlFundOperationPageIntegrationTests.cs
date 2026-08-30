using System.Data.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlFundOperationPageIntegrationTests
{
    [PostgreSqlFact]
    public async Task OperationsPageLoadsCountAndOnlyVisibleColumnsInOneCommand()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var firstFund = new Fund { Name = "Первый фонд", NormalizedName = "ПЕРВЫЙ ФОНД", SortOrder = 1 };
        var secondFund = new Fund { Name = "Второй фонд", NormalizedName = "ВТОРОЙ ФОНД", SortOrder = 2 };
        var first = CreateOperation(firstFund, new DateTimeOffset(2048, 1, 1, 10, 0, 0, TimeSpan.Zero), 100m);
        var second = CreateOperation(secondFund, new DateTimeOffset(2048, 1, 2, 10, 0, 0, TimeSpan.Zero), 200m);
        var third = CreateOperation(firstFund, new DateTimeOffset(2048, 1, 3, 10, 0, 0, TimeSpan.Zero), 300m, isCanceled: true);
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(first, second, third);
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var page = await new EfFundRepository(context)
            .GetOperationsPageAsync(offset: 1, limit: 1, includeCanceled: true, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal(second.Id, item.Id);
        Assert.Equal(secondFund.Id, item.FundId);
        Assert.Equal("Второй фонд", item.Fund.Name);
        Assert.Equal(200m, item.Amount);
        Assert.Equal("Операция 200", item.Reason);
        var command = Assert.Single(capture.Commands);
        Assert.Contains("COUNT(*)", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ActorUserId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static FundOperation CreateOperation(
        Fund fund,
        DateTimeOffset createdAtUtc,
        decimal amount,
        bool isCanceled = false) =>
        new()
        {
            Fund = fund,
            OperationKind = FundOperationKinds.Deposit,
            Amount = amount,
            BalanceBefore = 0m,
            BalanceAfter = amount,
            Reason = $"Операция {amount:0}",
            IsCanceled = isCanceled,
            ActorUserId = Guid.NewGuid(),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc.AddHours(1)
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
