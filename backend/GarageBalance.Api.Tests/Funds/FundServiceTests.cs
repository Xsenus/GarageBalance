using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
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
        Assert.All(funds, fund => Assert.Equal(0m, fund.AvailableToDistribute));
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
        await SeedIncomeAsync(database.Context, 2000m);

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
        var fundsAfterDeposit = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(fundsAfterDeposit, fund => Assert.Equal(499.87m, fund.AvailableToDistribute));

        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_deposited");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("funds", audit.Section);
        Assert.Equal("create", audit.ActionKind);
        Assert.Equal("fund_operation", audit.EntityType);
        Assert.Equal("Целевые взносы", audit.EntityDisplayName);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(AuditTextMasker.Mask(targetFund.Id.ToString()), metadata.RootElement.GetProperty("fundId").GetString());
        Assert.Equal("deposit", metadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("1500.13", metadata.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task CreateOperationAsync_DoesNotDepositMoreThanAvailableDistributionAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 2000m);

        var firstDeposit = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 500m, "Первичное распределение"),
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.True(firstDeposit.Succeeded);

        var fundsAfterFirstDeposit = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(fundsAfterFirstDeposit, fund => Assert.Equal(1500m, fund.AvailableToDistribute));

        var tooLargeDeposit = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 1500.01m, "Сверх свободного остатка"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(tooLargeDeposit.Succeeded);
        Assert.Equal("fund_distribution_amount_exceeded", tooLargeDeposit.ErrorCode);
        Assert.Equal(1, await database.Context.FundOperations.CountAsync());
        Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_deposited");
        var storedFund = await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id);
        Assert.Equal(500m, storedFund.Balance);
    }

    [Fact]
    public async Task CreateOperationAsync_WithdrawReturnsMoneyToAvailableDistributionAmount()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);

        Assert.True((await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 700m, "Распределение"),
            Guid.NewGuid(),
            CancellationToken.None)).Succeeded);

        var withdraw = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("withdraw", 250m, "Возврат в распределение"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(withdraw.Succeeded);
        var fundsAfterWithdraw = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(fundsAfterWithdraw, fund => Assert.Equal(550m, fund.AvailableToDistribute));
        Assert.Equal(450m, (await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id)).Balance);
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

    [Fact]
    public async Task GetOperationsAsync_ReturnsRecentOperationsAndCanIncludeCanceled()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 2000m);
        var first = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Первичное распределение"), null, CancellationToken.None);
        var second = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("withdraw", 100m, "Возврат"), null, CancellationToken.None);
        var third = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 300m, "Дополнительное распределение"), null, CancellationToken.None);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(third.Succeeded);
        Assert.True((await service.CancelOperationAsync(second.Value!.Id, new CancelFundOperationRequest("Ошибочное изъятие"), null, CancellationToken.None)).Succeeded);

        var activeOperations = await service.GetOperationsAsync(limit: 2, includeCanceled: false, CancellationToken.None);
        var allOperations = await service.GetOperationsAsync(limit: 10, includeCanceled: true, CancellationToken.None);

        Assert.Equal([third.Value!.Id, first.Value!.Id], activeOperations.Select(operation => operation.Id));
        Assert.Equal(3, allOperations.Count);
        Assert.Contains(allOperations, operation => operation.Id == second.Value.Id && operation.IsCanceled);
        Assert.All(allOperations, operation => Assert.Equal("Электроэнергия", operation.FundName));
    }

    [Fact]
    public async Task UpdateOperationAsync_UpdatesOperationRecalculatesBalancesAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var actorUserId = Guid.NewGuid();
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        var withdraw = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("withdraw", 200m, "Изъятие"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        Assert.True(withdraw.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOperationAsync(
            deposit.Value!.Id,
            new UpdateFundOperationRequest(600m, "Уточненное распределение"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(600m, result.Value!.Amount);
        Assert.Equal(0m, result.Value.BalanceBefore);
        Assert.Equal(600m, result.Value.BalanceAfter);
        Assert.Equal(400m, (await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id)).Balance);
        var updatedWithdraw = await database.Context.FundOperations.SingleAsync(operation => operation.Id == withdraw.Value!.Id);
        Assert.Equal(600m, updatedWithdraw.BalanceBefore);
        Assert.Equal(400m, updatedWithdraw.BalanceAfter);
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("update", audit.ActionKind);
        Assert.Equal("fund_operation", audit.EntityType);
        Assert.Equal("Электроэнергия", audit.EntityDisplayName);
        Assert.Contains("Изменена операция фонда Электроэнергия", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(AuditTextMasker.Mask(targetFund.Id.ToString()), metadata.RootElement.GetProperty("fundId").GetString());
        Assert.Equal("Электроэнергия", metadata.RootElement.GetProperty("fundName").GetString());
        Assert.Equal("deposit", metadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("600", metadata.RootElement.GetProperty("amount").GetString());
        Assert.Contains("Сумма", metadata.RootElement.GetProperty("changedFields").GetString(), StringComparison.Ordinal);
        Assert.Equal("3", metadata.RootElement.GetProperty("changesCount").GetString());
    }

    [Fact]
    public async Task UpdateOperationAsync_DoesNotWriteAuditWhenNothingChanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOperationAsync(
            deposit.Value!.Id,
            new UpdateFundOperationRequest(700m, "Распределение"),
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateOperationAsync_RejectsDepositIncreaseAboveAvailableDistribution()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOperationAsync(
            deposit.Value!.Id,
            new UpdateFundOperationRequest(1000.01m, "Сверх свободного остатка"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_distribution_amount_exceeded", result.ErrorCode);
        Assert.Equal(700m, (await database.Context.FundOperations.SingleAsync(operation => operation.Id == deposit.Value.Id)).Amount);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateOperationAsync_RejectsAmountWhenActiveSequenceWouldBecomeNegative()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        var withdraw = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("withdraw", 650m, "Изъятие"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        Assert.True(withdraw.Succeeded);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOperationAsync(
            deposit.Value!.Id,
            new UpdateFundOperationRequest(600m, "Уменьшили распределение"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.Equal(700m, (await database.Context.FundOperations.SingleAsync(operation => operation.Id == deposit.Value.Id)).Amount);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task UpdateOperationAsync_RejectsCanceledOperation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        await service.CancelOperationAsync(deposit.Value!.Id, new CancelFundOperationRequest("Ошибка"), null, CancellationToken.None);
        database.Context.AuditEvents.RemoveRange(database.Context.AuditEvents);
        await database.Context.SaveChangesAsync();

        var result = await service.UpdateOperationAsync(
            deposit.Value.Id,
            new UpdateFundOperationRequest(600m, "Изменение после отмены"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_operation_canceled", result.ErrorCode);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CancelOperationAsync_CancelsOperationRecalculatesBalanceAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var actorUserId = Guid.NewGuid();
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 700m, "Распределение"),
            null,
            CancellationToken.None);
        Assert.True(deposit.Succeeded);

        var result = await service.CancelOperationAsync(
            deposit.Value!.Id,
            new CancelFundOperationRequest("Ошибочное распределение"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCanceled);
        Assert.Contains("Отменено: Ошибочное распределение", result.Value.Reason, StringComparison.Ordinal);
        Assert.Equal(0m, (await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id)).Balance);
        var fundsAfterCancel = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(fundsAfterCancel, fund => Assert.Equal(1000m, fund.AvailableToDistribute));
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_canceled");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("cancel", audit.ActionKind);
        Assert.Contains("Отменена операция фонда Электроэнергия", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(AuditTextMasker.Mask(targetFund.Id.ToString()), metadata.RootElement.GetProperty("fundId").GetString());
        Assert.Equal("Электроэнергия", metadata.RootElement.GetProperty("fundName").GetString());
        Assert.Equal("deposit", metadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("700", metadata.RootElement.GetProperty("amount").GetString());
        Assert.Equal("True", metadata.RootElement.GetProperty("isCanceled").GetString());
        Assert.Equal("Ошибочное распределение", metadata.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CancelOperationAsync_RejectsDepositWhenActiveWithdrawWouldBecomeNegative()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        var withdraw = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("withdraw", 650m, "Изъятие"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        Assert.True(withdraw.Succeeded);

        var result = await service.CancelOperationAsync(
            deposit.Value!.Id,
            new CancelFundOperationRequest("Убрали распределение"),
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.False(await database.Context.FundOperations.AnyAsync(operation => operation.Id == deposit.Value.Id && operation.IsCanceled));
        Assert.Equal(50m, (await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id)).Balance);
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "fund.operation_canceled");
    }

    [Fact]
    public async Task RestoreOperationAsync_RestoresCanceledOperationRecalculatesBalanceAndWritesAudit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var actorUserId = Guid.NewGuid();
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(
            targetFund.Id,
            new CreateFundOperationRequest("deposit", 700m, "Распределение"),
            null,
            CancellationToken.None);
        await service.CancelOperationAsync(deposit.Value!.Id, new CancelFundOperationRequest("Ошибочное распределение"), null, CancellationToken.None);

        var result = await service.RestoreOperationAsync(deposit.Value.Id, actorUserId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCanceled);
        Assert.Equal(700m, (await database.Context.Funds.SingleAsync(fund => fund.Id == targetFund.Id)).Balance);
        var fundsAfterRestore = await service.GetFundsAsync(CancellationToken.None);
        Assert.All(fundsAfterRestore, fund => Assert.Equal(300m, fund.AvailableToDistribute));
        var audit = Assert.Single(database.Context.AuditEvents, item => item.Action == "fund.operation_restored");
        Assert.Equal(actorUserId, audit.ActorUserId);
        Assert.Equal("restore", audit.ActionKind);
        Assert.Contains("Восстановлена операция фонда Электроэнергия", audit.Summary, StringComparison.Ordinal);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(AuditTextMasker.Mask(targetFund.Id.ToString()), metadata.RootElement.GetProperty("fundId").GetString());
        Assert.Equal("Электроэнергия", metadata.RootElement.GetProperty("fundName").GetString());
        Assert.Equal("deposit", metadata.RootElement.GetProperty("operationKind").GetString());
        Assert.Equal("700", metadata.RootElement.GetProperty("amount").GetString());
        Assert.Equal("False", metadata.RootElement.GetProperty("isCanceled").GetString());
        Assert.False(metadata.RootElement.TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task RestoreOperationAsync_RejectsCanceledWithdrawWhenSequenceWouldBecomeNegative()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new FundService(database.Context, new AuditEventWriter(database.Context));
        var targetFund = (await service.GetFundsAsync(CancellationToken.None)).Single(fund => fund.Name == "Электроэнергия");
        await SeedIncomeAsync(database.Context, 1000m);
        var deposit = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("deposit", 700m, "Распределение"), null, CancellationToken.None);
        var withdraw = await service.CreateOperationAsync(targetFund.Id, new CreateFundOperationRequest("withdraw", 650m, "Изъятие"), null, CancellationToken.None);
        Assert.True(deposit.Succeeded);
        Assert.True(withdraw.Succeeded);
        await service.CancelOperationAsync(withdraw.Value!.Id, new CancelFundOperationRequest("Возврат"), null, CancellationToken.None);
        await service.CancelOperationAsync(deposit.Value!.Id, new CancelFundOperationRequest("Убрали распределение"), null, CancellationToken.None);

        var result = await service.RestoreOperationAsync(withdraw.Value.Id, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("fund_balance_insufficient", result.ErrorCode);
        Assert.True(await database.Context.FundOperations.AnyAsync(operation => operation.Id == withdraw.Value.Id && operation.IsCanceled));
        Assert.DoesNotContain(database.Context.AuditEvents, item => item.Action == "fund.operation_restored");
    }

    private static async Task SeedIncomeAsync(GarageBalanceDbContext context, decimal amount)
    {
        var garage = new Garage { Number = $"G-{Guid.NewGuid():N}", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = $"Поступление {Guid.NewGuid():N}" };
        context.AddRange(garage, incomeType);
        context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            Garage = garage,
            IncomeType = incomeType,
            OperationDate = new DateOnly(2026, 6, 19),
            AccountingMonth = new DateOnly(2026, 6, 1),
            Amount = amount
        });

        await context.SaveChangesAsync();
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
