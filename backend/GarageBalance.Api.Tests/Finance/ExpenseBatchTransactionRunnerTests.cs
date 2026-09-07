using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpenseBatchTransactionRunnerTests
{
    [PostgreSqlFact]
    public async Task ExecuteAsync_CommitsAllSavesAndClearsTracking()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = NewFund();
        var result = await new EfExpenseBatchTransactionRunner(context).ExecuteAsync(async token =>
        {
            context.Add(fund);
            await context.SaveChangesAsync(token);
            fund.Balance = 25m;
            await context.SaveChangesAsync(token);
            return FinanceResult<Guid>.Success(fund.Id);
        }, CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(fund.Id, result.Value);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var check = database.CreateContext();
        Assert.Equal(25m, (await check.Funds.SingleAsync(item => item.Id == fund.Id)).Balance);
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_RollsBackEarlierSavesForBusinessFailureExceptionCancellationAndUnsavedChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        foreach (var scenario in new[] { "failure", "exception", "cancellation", "unsaved" })
        {
            await using var context = database.CreateContext();
            var fund = NewFund();
            using var cancellation = new CancellationTokenSource();
            async Task<FinanceResult<Guid>> Action(CancellationToken token)
            {
                context.Add(fund);
                await context.SaveChangesAsync(token);
                if (scenario == "exception") throw new InvalidOperationException("Контроль отката");
                if (scenario == "cancellation")
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                if (scenario == "unsaved")
                {
                    fund.Balance = 99m;
                    return FinanceResult<Guid>.Success(fund.Id);
                }
                return FinanceResult<Guid>.Failure("bank_amount_insufficient", "Недостаточно средств");
            }
            var runner = new EfExpenseBatchTransactionRunner(context);
            if (scenario == "cancellation")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync(Action, cancellation.Token));
            else if (scenario is "exception" or "unsaved")
                await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(Action, cancellation.Token));
            else
                Assert.Equal("bank_amount_insufficient", (await runner.ExecuteAsync(Action, cancellation.Token)).ErrorCode);
            Assert.Empty(context.ChangeTracker.Entries());
            await context.SaveChangesAsync();
            await using var check = database.CreateContext();
            Assert.False(await check.Funds.AnyAsync(item => item.Id == fund.Id), scenario);
        }
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_RejectsPreexistingChangesAndNestedTransactionWithoutLosingCallerWork()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = NewFund();
        context.Add(fund);
        var runner = new EfExpenseBatchTransactionRunner(context);
        var calls = 0;
        Task<FinanceResult<int>> Action(CancellationToken token) { calls++; return Task.FromResult(FinanceResult<int>.Success(1)); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(Action, CancellationToken.None));
        Assert.Equal(EntityState.Added, context.Entry(fund).State);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(Action, CancellationToken.None));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(0, calls);
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_ReportsRealSerializationConflictWithoutRetryOrPartialCommit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = NewFund();
        context.Add(fund);
        await context.SaveChangesAsync();
        var calls = 0;
        var result = await new EfExpenseBatchTransactionRunner(context).ExecuteAsync(async token =>
        {
            calls++;
            var current = await context.Funds.SingleAsync(item => item.Id == fund.Id, token);
            await using var competing = database.CreateContext();
            var other = await competing.Funds.SingleAsync(item => item.Id == fund.Id, token);
            other.Balance = 40m;
            await competing.SaveChangesAsync(token);
            current.Balance = 90m;
            await context.SaveChangesAsync(token);
            return FinanceResult<int>.Success(1);
        }, CancellationToken.None);
        Assert.Equal("expense_batch_concurrency", result.ErrorCode);
        Assert.Equal(1, calls);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(40m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_MapsDirectDeadlockAndWrappedSerializationErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        foreach (var wrap in new[] { false, true })
        {
            await using var context = database.CreateContext();
            var exception = new PostgresException("Conflict", "ERROR", "ERROR", wrap ? "40001" : "40P01");
            var result = await new EfExpenseBatchTransactionRunner(context).ExecuteAsync<int>(token =>
                Task.FromException<FinanceResult<int>>(wrap ? new DbUpdateException("Conflict", exception) : exception), CancellationToken.None);
            Assert.Equal("expense_batch_concurrency", result.ErrorCode);
        }
        await using var otherContext = database.CreateContext();
        var unexpected = await Assert.ThrowsAsync<PostgresException>(() => new EfExpenseBatchTransactionRunner(otherContext)
            .ExecuteAsync<int>(token => Task.FromException<FinanceResult<int>>(
                new PostgresException("Unexpected", "ERROR", "ERROR", "23505")), CancellationToken.None));
        Assert.Equal("23505", unexpected.SqlState);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingActionAndCanceledRequestBeforeDatabaseAccess()
    {
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().Options);
        var runner = new EfExpenseBatchTransactionRunner(context);
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.ExecuteAsync<int>(null!, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync(
            token => Task.FromResult(FinanceResult<int>.Success(1)), new CancellationToken(true)));
    }

    private static Fund NewFund() => new()
    {
        Name = $"Контроль транзакции {Guid.NewGuid():N}",
        NormalizedName = Guid.NewGuid().ToString("N"),
        AllowOperations = true
    };
}
