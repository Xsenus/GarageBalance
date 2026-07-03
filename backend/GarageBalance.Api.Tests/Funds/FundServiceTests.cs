using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Funds;

public sealed class FundServiceTests
{
    [Fact]
    public async Task GetFundsAsync_SeedsDefaultFunds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));

        var funds = await service.GetFundsAsync(CancellationToken.None);

        Assert.Equal(7, funds.Count);
        Assert.Equal("Электроэнергия", funds[0].Name);
        Assert.Equal("Прочее", funds[^1].Name);
        Assert.False(funds.Single(fund => fund.Name == "Членские взносы").AllowOperations);
        Assert.Equal(7, await database.Context.Funds.CountAsync());
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateOperationAsync_DepositsMoneyAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var actorUserId = Guid.NewGuid();
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Целевые взносы");

        var result = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 1500.129m, "Решение правления"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(FundOperationKinds.Deposit, result.Value!.OperationKind);
        Assert.Equal(1500.13m, result.Value.Amount);
        Assert.Equal(0m, result.Value.BalanceBefore);
        Assert.Equal(1500.13m, result.Value.BalanceAfter);
        var storedFund = await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id);
        Assert.Equal(1500.13m, storedFund.Balance);

        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_deposited");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("funds", audit.Section);
        Assert.Equal("create", audit.ActionKind);
        Assert.Equal("fund_operation", audit.EntityType);
        Assert.Equal("Целевые взносы", audit.EntityDisplayName);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(targetFund.Id.ToString(), metadata.RootElement.GetProperty("fundId").GetString());
        Assert.Equal("deposit", metadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("1500.13", metadata.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task CreateOperationAsync_DoesNotWithdrawMoreThanBalance()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");

        var result = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("withdraw", 1m, "Проверка лимита"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.Empty(database.Context.FundOperations);
        Assert.Empty(database.Context.AuditEvents);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private TestDatabase(SqliteConnection connection, GarageBalanceDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        public GarageBalanceDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new GarageBalanceDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
