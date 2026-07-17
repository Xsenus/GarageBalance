using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlPaymentAllocationIntegrationTests
{
    [PostgreSqlFact]
    public async Task PaymentAllocation_UsesFifo_SerializesConcurrentPayments_AndEnforcesDatabaseConstraints()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var fifo = await SeedLedgerAsync(database, "PG-FIFO", [60m, 70m]);

        await using (var paymentContext = database.CreateContext())
        {
            var result = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    fifo.GarageId,
                    fifo.IncomeTypeId,
                    new DateOnly(2026, 3, 10),
                    new DateOnly(2026, 3, 1),
                    100m,
                    "PG-FIFO-1",
                    null),
                null,
                CancellationToken.None);

            Assert.True(result.Succeeded);
        }

        await using (var assertionContext = database.CreateContext())
        {
            var fifoAllocations = await assertionContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && fifo.AccrualIds.Contains(item.AccrualId))
                .OrderBy(item => item.Accrual.DueDate)
                .Select(item => item.Amount)
                .ToArrayAsync();
            Assert.Equal([60m, 40m], fifoAllocations);
        }

        await VerifyDatabaseConstraintsAsync(database, fifo.AccrualIds[0]);

        var concurrent = await SeedLedgerAsync(database, "PG-CONCURRENT", [100m]);
        await VerifyAdvisoryLockScopeAsync(database, fifo, concurrent);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = FinanceServiceTestFactory.Create(firstContext);
        var secondService = FinanceServiceTestFactory.Create(secondContext);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstPayment = Task.Run(async () =>
        {
            await start.Task;
            return await firstService.CreateIncomeAsync(
                CreateConcurrentPayment(concurrent, "PG-CONCURRENT-1"),
                null,
                CancellationToken.None);
        });
        var secondPayment = Task.Run(async () =>
        {
            await start.Task;
            return await secondService.CreateIncomeAsync(
                CreateConcurrentPayment(concurrent, "PG-CONCURRENT-2"),
                null,
                CancellationToken.None);
        });

        start.SetResult();
        var results = await Task.WhenAll(firstPayment, secondPayment);
        Assert.All(results, result => Assert.True(result.Succeeded));

        await using var concurrentAssertionContext = database.CreateContext();
        var activeAllocations = await concurrentAssertionContext.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.AccrualId == concurrent.AccrualIds[0])
            .OrderBy(item => item.FinancialOperation.OperationDate)
            .ThenBy(item => item.FinancialOperation.CreatedAtUtc)
            .Select(item => item.Amount)
            .ToArrayAsync();
        var inactiveAllocationCount = await concurrentAssertionContext.AccrualPaymentAllocations
            .CountAsync(item => !item.IsActive && item.AccrualId == concurrent.AccrualIds[0]);

        Assert.Equal(100m, activeAllocations.Sum());
        Assert.Equal([70m, 30m], activeAllocations);
        Assert.True(inactiveAllocationCount > 0);
    }

    private static async Task VerifyAdvisoryLockScopeAsync(
        PostgreSqlTestDatabase database,
        SeededLedger first,
        SeededLedger second)
    {
        await using var ownerContext = database.CreateContext();
        await using var independentContext = database.CreateContext();
        await using var waiterContext = database.CreateContext();
        var firstKey = new AccrualPaymentAllocationKey(first.GarageId, first.IncomeTypeId);
        var secondKey = new AccrualPaymentAllocationKey(second.GarageId, second.IncomeTypeId);
        var ownerRepository = new EfAccrualPaymentAllocationRepository(ownerContext);
        var independentRepository = new EfAccrualPaymentAllocationRepository(independentContext);
        var waiterRepository = new EfAccrualPaymentAllocationRepository(waiterContext);
        var ownerLease = await ownerRepository.AcquireRebuildLockAsync([firstKey], CancellationToken.None);

        await using var independentLease = await independentRepository
            .AcquireRebuildLockAsync([secondKey], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        var waiterTask = waiterRepository.AcquireRebuildLockAsync([firstKey], CancellationToken.None);
        await Task.Delay(200);
        Assert.False(waiterTask.IsCompleted);

        await ownerLease.DisposeAsync();
        await using var waiterLease = await waiterTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static CreateIncomeOperationRequest CreateConcurrentPayment(
        SeededLedger ledger,
        string documentNumber) =>
        new(
            ledger.GarageId,
            ledger.IncomeTypeId,
            new DateOnly(2026, 4, 10),
            new DateOnly(2026, 4, 1),
            70m,
            documentNumber,
            null);

    private static async Task<SeededLedger> SeedLedgerAsync(
        PostgreSqlTestDatabase database,
        string suffix,
        IReadOnlyList<decimal> amounts)
    {
        await using var context = database.CreateContext();
        var garage = new Garage { Number = suffix, PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = suffix };
        var accruals = amounts.Select((amount, index) => new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, index + 1, 1),
            DueDate = new DateOnly(2026, index + 1, 28),
            OverdueFromDate = new DateOnly(2026, index + 2, 1),
            Amount = amount,
            Source = AccrualSources.Manual
        }).ToArray();
        context.AddRange(accruals);
        await context.SaveChangesAsync();
        return new SeededLedger(garage.Id, incomeType.Id, accruals.Select(item => item.Id).ToArray());
    }

    private static async Task VerifyDatabaseConstraintsAsync(
        PostgreSqlTestDatabase database,
        Guid accrualId)
    {
        Guid operationId;
        await using (var readContext = database.CreateContext())
        {
            operationId = await readContext.AccrualPaymentAllocations
                .Where(item => item.IsActive && item.AccrualId == accrualId)
                .Select(item => item.FinancialOperationId)
                .FirstAsync();
        }

        await using (var amountContext = database.CreateContext())
        {
            amountContext.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
            {
                FinancialOperationId = operationId,
                AccrualId = accrualId,
                Amount = 0m,
                IsActive = false
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => amountContext.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, GetPostgresException(exception).SqlState);
        }

        await using (var uniqueContext = database.CreateContext())
        {
            uniqueContext.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
            {
                FinancialOperationId = operationId,
                AccrualId = accrualId,
                Amount = 1m,
                IsActive = true
            });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => uniqueContext.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, GetPostgresException(exception).SqlState);
        }
    }

    private static PostgresException GetPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        throw new Xunit.Sdk.XunitException("Expected a PostgreSQL server exception.");
    }

    private sealed record SeededLedger(Guid GarageId, Guid IncomeTypeId, Guid[] AccrualIds);
}
