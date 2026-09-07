using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Users;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class ExpensePaymentBatchRepositoryTests
{
    [PostgreSqlFact]
    public async Task FindAsync_ReturnsPersistedResultOnlyToItsAuthorAndPreservesCanceledOperationLink()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var author = User();
        var operation = Operation();
        context.AddRange(author, operation);
        await context.SaveChangesAsync();
        var batch = Batch(author.Id, operation.Id);
        new EfExpensePaymentBatchRepository(context).Add(batch);
        await context.SaveChangesAsync();
        operation.IsCanceled = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new EfExpensePaymentBatchRepository(context);
        var found = await repository.FindAsync(batch.Id, author.Id, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(batch.RequestHash, found.RequestHash);
        Assert.Equal(operation.Id, Assert.Single(found.Operations).OperationId);
        Assert.Null(await repository.FindAsync(batch.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await repository.FindAsync(Guid.NewGuid(), author.Id, CancellationToken.None));
        Assert.Empty(context.ChangeTracker.Entries());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.FindAsync(batch.Id, author.Id, new CancellationToken(true)));
        var rollback = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync("20260905214500_RouteUnusedDefaultFeeFundsToOther"));
        Assert.Contains("Нельзя удалить историю", rollback.MessageText);
        Assert.NotNull(await repository.FindAsync(batch.Id, author.Id, CancellationToken.None));
    }

    [PostgreSqlFact]
    public async Task Save_RejectsDuplicateRequestAndOperationWithoutCreatingAnotherBatch()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var author = User();
        var operation = Operation();
        context.AddRange(author, operation);
        await context.SaveChangesAsync();
        var batch = Batch(author.Id, operation.Id);
        context.Add(batch);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        foreach (var sameRequest in new[] { true, false })
        {
            var duplicate = Batch(author.Id, operation.Id);
            if (sameRequest) duplicate.Id = batch.Id;
            context.Add(duplicate);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
            context.ChangeTracker.Clear();
            Assert.Equal(1, await context.ExpensePaymentBatches.CountAsync());
        }
    }

    [PostgreSqlFact]
    public async Task Save_RollsBackBatchTogetherWithPaymentAndRestrictsRemovalOfStoredResult()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var author = User();
        context.Add(author);
        await context.SaveChangesAsync();
        var operation = Operation();
        var batch = Batch(author.Id, operation.Id);
        var result = await new EfExpenseBatchTransactionRunner(context).ExecuteAsync(async token =>
        {
            context.Add(operation);
            await context.SaveChangesAsync(token);
            new EfExpensePaymentBatchRepository(context).Add(batch);
            await context.SaveChangesAsync(token);
            return FinanceResult<int>.Failure("test_failure", "Проверка отката");
        }, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.False(await context.FinancialOperations.AnyAsync(item => item.Id == operation.Id));
        Assert.False(await context.ExpensePaymentBatches.AnyAsync(item => item.Id == batch.Id));

        context.Add(operation);
        context.Add(batch);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var paymentDelete = await Assert.ThrowsAsync<PostgresException>(() => context.FinancialOperations.Where(item => item.Id == operation.Id).ExecuteDeleteAsync());
        var authorDelete = await Assert.ThrowsAsync<PostgresException>(() => context.Users.Where(item => item.Id == author.Id).ExecuteDeleteAsync());
        var batchDelete = await Assert.ThrowsAsync<PostgresException>(() => context.ExpensePaymentBatches.Where(item => item.Id == batch.Id).ExecuteDeleteAsync());
        Assert.All(new[] { paymentDelete, authorDelete, batchDelete }, exception => Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState));
    }

    [PostgreSqlFact]
    public async Task Migration_UpgradesExistingPaymentsWithoutChangingThem()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20260905214500_RouteUnusedDefaultFeeFundsToOther");
        await using var context = database.CreateContext();
        var operation = Operation();
        context.Add(operation);
        await context.SaveChangesAsync();
        var version = operation.Version;
        await context.Database.MigrateAsync();
        await context.Database.MigrateAsync("20260905214500_RouteUnusedDefaultFeeFundsToOther");
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        var saved = await context.FinancialOperations.SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(10m, saved.Amount);
        Assert.Equal(version, saved.Version);
        Assert.Empty(await context.ExpensePaymentBatches.ToListAsync());
    }

    private static AppUser User() => new()
    {
        Email = "batch-test@example.test",
        NormalizedEmail = "BATCH-TEST@EXAMPLE.TEST",
        DisplayName = "Контроль пакета",
        PasswordHash = "not-a-real-password-hash"
    };
    private static FinancialOperation Operation() => new()
    {
        OperationKind = "expense",
        OperationDate = new DateOnly(2026, 8, 1),
        AccountingMonth = new DateOnly(2026, 8, 1),
        Amount = 10m,
        ExpensePaymentSource = "cash"
    };
    private static ExpensePaymentBatch Batch(Guid author, Guid operation) => new()
    {
        Id = Guid.NewGuid(),
        ActorUserId = author,
        RequestHash = new string('a', 64),
        Operations = [new() { OperationId = operation }]
    };
}
