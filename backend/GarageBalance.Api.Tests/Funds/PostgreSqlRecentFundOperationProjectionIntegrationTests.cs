using System.Data.Common;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlRecentFundOperationProjectionIntegrationTests
{
    [PostgreSqlFact]
    public async Task RecentOperationsUseOneBoundedCompactCommandAndPreserveDisplayedData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fund = new Fund
        {
            Name = "Резервный фонд",
            NormalizedName = "РЕЗЕРВНЫЙ ФОНД",
            Balance = 999_999m,
            SortOrder = 17,
            AllowOperations = true,
            IsSystem = false,
            IsArchived = false,
            CreatedAtUtc = new DateTimeOffset(2049, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2049, 1, 2, 0, 0, 0, TimeSpan.Zero),
            Version = Guid.NewGuid()
        };
        var oldest = CreateOperation(fund, new DateTimeOffset(2049, 2, 1, 10, 0, 0, TimeSpan.Zero), 100m);
        var active = CreateOperation(fund, new DateTimeOffset(2049, 2, 2, 10, 0, 0, TimeSpan.Zero), 250m);
        var canceled = CreateOperation(
            fund,
            new DateTimeOffset(2049, 2, 3, 10, 0, 0, TimeSpan.Zero),
            75m,
            operationKind: FundOperationKinds.Withdraw,
            isCanceled: true);
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(oldest, active, canceled);
            await seedContext.SaveChangesAsync();
        }

        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var items = await new EfFundRepository(context)
            .GetRecentOperationsAsync(limit: 2, includeCanceled: true, CancellationToken.None);

        Assert.Equal([canceled.Id, active.Id], items.Select(item => item.Id));
        var first = items[0];
        Assert.Equal(fund.Id, first.FundId);
        Assert.Equal("Резервный фонд", first.Fund.Name);
        Assert.Equal(FundOperationKinds.Withdraw, first.OperationKind);
        Assert.Equal(75m, first.Amount);
        Assert.Equal(0m, first.BalanceBefore);
        Assert.Equal(-75m, first.BalanceAfter);
        Assert.Equal("Операция 75", first.Reason);
        Assert.True(first.IsCanceled);
        Assert.Equal(canceled.CreatedAtUtc, first.CreatedAtUtc);
        Assert.Null(first.SourceFinancialOperationId);

        var command = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SourceFinancialOperationId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ActorUserId", command, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdatedAtUtc", command, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedName", command, StringComparison.Ordinal);
        Assert.DoesNotContain("SortOrder", command, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowOperations", command, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSystem", command, StringComparison.Ordinal);
        Assert.DoesNotContain("IsArchived", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Version", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RecentOperationsExcludeCanceledRowsBeforeApplyingLimit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fund = new Fund { Name = "Целевой фонд", NormalizedName = "ЦЕЛЕВОЙ ФОНД" };
        var active = CreateOperation(fund, new DateTimeOffset(2049, 3, 1, 10, 0, 0, TimeSpan.Zero), 400m);
        var canceled = CreateOperation(
            fund,
            new DateTimeOffset(2049, 3, 2, 10, 0, 0, TimeSpan.Zero),
            500m,
            isCanceled: true);
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AddRange(active, canceled);
            await seedContext.SaveChangesAsync();
        }

        await using var context = database.CreateContext();
        var item = Assert.Single(await new EfFundRepository(context)
            .GetRecentOperationsAsync(limit: 1, includeCanceled: false, CancellationToken.None));

        Assert.Equal(active.Id, item.Id);
        Assert.False(item.IsCanceled);
    }

    [PostgreSqlFact]
    public async Task RecentOperationsHonorCancellationBeforeReadingDatabase()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var capture = new ReaderCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(capture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfFundRepository(context)
            .GetRecentOperationsAsync(limit: 25, includeCanceled: false, cancellation.Token));

        Assert.Empty(capture.Commands);
    }

    private static FundOperation CreateOperation(
        Fund fund,
        DateTimeOffset createdAtUtc,
        decimal amount,
        string operationKind = FundOperationKinds.Deposit,
        bool isCanceled = false) =>
        new()
        {
            Fund = fund,
            OperationKind = operationKind,
            Amount = amount,
            BalanceBefore = 0m,
            BalanceAfter = operationKind == FundOperationKinds.Deposit ? amount : -amount,
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
