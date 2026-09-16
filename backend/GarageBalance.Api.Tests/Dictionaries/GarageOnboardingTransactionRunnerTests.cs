using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageOnboardingTransactionRunnerTests
{
    [PostgreSqlFact]
    public async Task ExecuteAsync_CommitsSuccessfulActionAndClearsTracking()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var fund = NewFund();

        var result = await new EfGarageOnboardingTransactionRunner(context).ExecuteAsync(async token =>
        {
            context.Add(fund);
            await context.SaveChangesAsync(token);
            return DictionaryResult<Guid>.Success(fund.Id);
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(fund.Id, result.Value);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var check = database.CreateContext();
        Assert.True(await check.Funds.AnyAsync(item => item.Id == fund.Id));
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_RollsBackBusinessFailureExceptionAndUnsavedChanges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        foreach (var scenario in new[] { "failure", "exception", "unsaved" })
        {
            await using var context = database.CreateContext();
            var fund = NewFund();
            async Task<DictionaryResult<Guid>> Action(CancellationToken token)
            {
                context.Add(fund);
                await context.SaveChangesAsync(token);
                if (scenario == "exception") throw new InvalidOperationException("Контроль отката");
                if (scenario == "unsaved")
                {
                    fund.Balance = 10m;
                    return DictionaryResult<Guid>.Success(fund.Id);
                }
                return DictionaryResult<Guid>.Failure("annual_payment_invalid", "Контроль отката");
            }

            var runner = new EfGarageOnboardingTransactionRunner(context);
            if (scenario is "exception" or "unsaved")
                await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync(Action, CancellationToken.None));
            else
                Assert.Equal("annual_payment_invalid", (await runner.ExecuteAsync(Action, CancellationToken.None)).ErrorCode);

            Assert.Empty(context.ChangeTracker.Entries());
            await using var check = database.CreateContext();
            Assert.False(await check.Funds.AnyAsync(item => item.Id == fund.Id), scenario);
        }
    }

    [PostgreSqlFact]
    public async Task ExecuteAsync_MapsSerializationConflictsAndRejectsOtherPostgresErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var conflict = new PostgresException("Conflict", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        var result = await new EfGarageOnboardingTransactionRunner(context).ExecuteAsync<int>(
            _ => Task.FromException<DictionaryResult<int>>(conflict), CancellationToken.None);
        Assert.Equal("garage_onboarding_concurrency", result.ErrorCode);

        await using var otherContext = database.CreateContext();
        var unexpected = await Assert.ThrowsAsync<PostgresException>(() => new EfGarageOnboardingTransactionRunner(otherContext)
            .ExecuteAsync<int>(_ => Task.FromException<DictionaryResult<int>>(
                new PostgresException("Unexpected", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)), CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, unexpected.SqlState);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingActionAndCanceledRequestBeforeDatabaseAccess()
    {
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().Options);
        var runner = new EfGarageOnboardingTransactionRunner(context);
        await Assert.ThrowsAsync<ArgumentNullException>(() => runner.ExecuteAsync<int>(null!, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync(
            _ => Task.FromResult(DictionaryResult<int>.Success(1)), new CancellationToken(true)));
    }

    private static Fund NewFund() => new()
    {
        Name = $"Контроль добавления гаража {Guid.NewGuid():N}",
        NormalizedName = Guid.NewGuid().ToString("N"),
        AllowOperations = true
    };
}
