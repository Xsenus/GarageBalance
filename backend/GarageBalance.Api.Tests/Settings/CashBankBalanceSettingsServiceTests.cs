using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Settings;

public sealed class CashBankBalanceSettingsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_ReturnsZeroBalancesAndEmptyHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(0m, result.CashOpeningBalance);
        Assert.Equal(0m, result.BankOpeningBalance);
        Assert.Equal(0m, result.CashCurrentBalance);
        Assert.Equal(0m, result.BankCurrentBalance);
        Assert.Empty(result.RecentOperations);
    }

    [Fact]
    public async Task GetAsync_UsesTheSameLegacyCashExpenseClassificationAsFinanceJournal()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var expenseType = new ExpenseType
        {
            Name = "Старое наименование",
            Code = "advance_payment"
        };
        database.Context.ExpenseTypes.Add(expenseType);
        database.Context.FinancialOperations.AddRange(
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 7, 1),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 100m
            },
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Expense,
                OperationDate = new DateOnly(2026, 7, 2),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 25m,
                ExpenseType = expenseType
            });
        await database.Context.SaveChangesAsync();
        var service = CreateService(database);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(75m, result.CashCurrentBalance);
        Assert.Equal(0m, result.BankCurrentBalance);
    }

    [Fact]
    public async Task UpdateOpeningBalances_CreatesImmutableOperationsAndAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var actorUserId = Guid.NewGuid();
        var service = CreateService(database);

        var result = await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(1250.126m, 8200m, "Остатки на начало учёта"),
            actorUserId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1250.13m, result.Value!.CashOpeningBalance);
        Assert.Equal(8200m, result.Value.BankOpeningBalance);
        Assert.Equal(1250.13m, result.Value.CashCurrentBalance);
        Assert.Equal(8200m, result.Value.BankCurrentBalance);
        var operations = await database.Context.CashBankBalanceOperations
            .OrderBy(operation => operation.Account)
            .ToListAsync();
        Assert.Equal(2, operations.Count);
        Assert.All(operations, operation =>
        {
            Assert.Equal(CashBankBalanceOperationKinds.OpeningBalance, operation.OperationKind);
            Assert.Equal(CashBankBalanceDirections.Increase, operation.Direction);
            Assert.Equal(new DateOnly(2026, 7, 27), operation.OperationDate);
            Assert.Equal(actorUserId, operation.ActorUserId);
        });
        var audit = await database.Context.AuditEvents.SingleAsync();
        Assert.Equal("cash_bank_opening_balances.updated", audit.Action);
        Assert.Equal(actorUserId, audit.ActorUserId);
    }

    [Fact]
    public async Task UpdateOpeningBalances_UsesDeltaAndDoesNotRewriteHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);
        await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(1000m, 500m, "Первичная настройка"),
            Guid.NewGuid(),
            CancellationToken.None);

        var result = await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(750m, 500m, "Исправление документа"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(750m, result.Value!.CashOpeningBalance);
        var cashOperations = await database.Context.CashBankBalanceOperations
            .Where(operation => operation.Account == CashBankAccounts.Cash)
            .ToListAsync();
        Assert.Equal(2, cashOperations.Count);
        Assert.Contains(cashOperations, operation =>
            operation.Direction == CashBankBalanceDirections.Increase &&
            operation.Amount == 1000m);
        Assert.Contains(cashOperations, operation =>
            operation.Direction == CashBankBalanceDirections.Decrease &&
            operation.Amount == 250m);
    }

    [Fact]
    public async Task CreateAdjustment_IncreasesAndDecreasesSelectedBalance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);
        await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(100m, 200m, "Первичная настройка"),
            Guid.NewGuid(),
            CancellationToken.None);

        var increased = await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                CashBankAccounts.Cash,
                CashBankBalanceDirections.Increase,
                new DateOnly(2026, 7, 26),
                50.555m,
                "Размен кассы"),
            Guid.NewGuid(),
            CancellationToken.None);
        var decreased = await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                CashBankAccounts.Bank,
                CashBankBalanceDirections.Decrease,
                new DateOnly(2026, 7, 27),
                25m,
                "Банковская комиссия"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(increased.Succeeded);
        Assert.True(decreased.Succeeded);
        Assert.Equal(150.56m, decreased.Value!.CashCurrentBalance);
        Assert.Equal(175m, decreased.Value.BankCurrentBalance);
        Assert.Equal(4, await database.Context.CashBankBalanceOperations.CountAsync());
        Assert.Equal(3, await database.Context.AuditEvents.CountAsync());
    }

    [Theory]
    [InlineData("", "increase", 10, "Причина", "account_invalid")]
    [InlineData("cash", "unknown", 10, "Причина", "direction_invalid")]
    [InlineData("cash", "increase", 0, "Причина", "amount_invalid")]
    [InlineData("cash", "increase", 10, "x", "reason_invalid")]
    public async Task CreateAdjustment_RejectsInvalidInputWithoutWriting(
        string account,
        string direction,
        decimal amount,
        string reason,
        string errorCode)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);

        var result = await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                account,
                direction,
                new DateOnly(2026, 7, 27),
                amount,
                reason),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(database.Context.CashBankBalanceOperations);
        Assert.Empty(database.Context.AuditEvents);
    }

    [Fact]
    public async Task CreateAdjustment_RejectsWriteOffAboveAvailableBalance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);
        await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(100m, 0m, "Первичная настройка"),
            Guid.NewGuid(),
            CancellationToken.None);

        var result = await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                CashBankAccounts.Cash,
                CashBankBalanceDirections.Decrease,
                new DateOnly(2026, 7, 27),
                100.01m,
                "Списание"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("insufficient_balance", result.ErrorCode);
        Assert.Single(database.Context.CashBankBalanceOperations);
    }

    [Fact]
    public async Task UpdateOpeningBalances_RejectsValueThatWouldMakeCurrentBalanceNegative()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);
        await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(100m, 0m, "Первичная настройка"),
            Guid.NewGuid(),
            CancellationToken.None);
        await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                CashBankAccounts.Cash,
                CashBankBalanceDirections.Decrease,
                new DateOnly(2026, 7, 27),
                80m,
                "Выдача под отчёт"),
            Guid.NewGuid(),
            CancellationToken.None);

        var result = await service.UpdateOpeningBalancesAsync(
            new UpdateCashBankOpeningBalancesRequest(50m, 0m, "Ошибочное уменьшение"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("opening_balance_below_committed_amount", result.ErrorCode);
        Assert.Equal(2, await database.Context.CashBankBalanceOperations.CountAsync());
    }

    [Fact]
    public async Task CreateAdjustment_RejectsMissingOperationDate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);

        var result = await service.CreateAdjustmentAsync(
            new CreateCashBankBalanceAdjustmentRequest(
                CashBankAccounts.Cash,
                CashBankBalanceDirections.Increase,
                default,
                10m,
                "Пополнение"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("operation_date_required", result.ErrorCode);
        Assert.Empty(database.Context.CashBankBalanceOperations);
    }

    [Fact]
    public async Task GetAsync_ReturnsOnlyFiftyMostRecentOperations()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        database.Context.CashBankBalanceOperations.AddRange(
            Enumerable.Range(1, 55).Select(index => new CashBankBalanceOperation
            {
                Account = CashBankAccounts.Cash,
                OperationKind = CashBankBalanceOperationKinds.Adjustment,
                Direction = CashBankBalanceDirections.Increase,
                OperationDate = new DateOnly(2026, 6, 1).AddDays(index),
                Amount = index,
                Reason = $"Операция {index}"
            }));
        await database.Context.SaveChangesAsync();
        var service = CreateService(database);

        var result = await service.GetAsync(CancellationToken.None);

        Assert.Equal(50, result.RecentOperations.Count);
        Assert.Equal(new DateOnly(2026, 7, 26), result.RecentOperations[0].OperationDate);
        Assert.DoesNotContain(result.RecentOperations, operation => operation.Reason == "Операция 1");
    }

    [Fact]
    public async Task GetAsync_PropagatesCancellation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var service = CreateService(database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAsync(cancellation.Token));
    }

    private static CashBankBalanceSettingsService CreateService(SqliteTestDatabase database)
    {
        var timeProvider = new FixedTimeProvider(Now);
        return new CashBankBalanceSettingsService(
            new EfCashBankBalanceOperationRepository(database.Context),
            new EfFinanceAvailableBalanceQuery(database.Context),
            new EfApplicationUnitOfWork(database.Context),
            new GarageBalance.Api.Application.Audit.AuditEventWriter(database.Context),
            new TestBusinessDateProvider(new DateOnly(2026, 7, 27)),
            timeProvider);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
