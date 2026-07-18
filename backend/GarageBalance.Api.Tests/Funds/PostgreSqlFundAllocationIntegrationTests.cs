using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Funds;

public sealed class PostgreSqlFundAllocationIntegrationTests
{
    private const long FundAllocationLockKey = 0x474246554E44;

    [PostgreSqlFact]
    public async Task ConcurrentDepositsToDifferentFunds_CannotDistributeSameIncomeTwice()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid firstFundId;
        Guid secondFundId;
        await using (var setupContext = database.CreateContext())
        {
            var setupService = CreateService(setupContext);
            var funds = await setupService.GetFundsAsync(CancellationToken.None);
            firstFundId = funds.First(fund => fund.AllowOperations).Id;
            secondFundId = funds.Last(fund => fund.AllowOperations && fund.Id != firstFundId).Id;
            await SeedIncomeAsync(setupContext, 100m);
        }

        await using var blockerConnection = new NpgsqlConnection(database.ConnectionString);
        await blockerConnection.OpenAsync();
        await ExecuteLockCommandAsync(blockerConnection, "SELECT pg_advisory_lock(@lock_key)");

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstTask = CreateService(firstContext).CreateOperationAsync(
            firstFundId,
            new CreateFundOperationRequest("deposit", 80m, "Первое параллельное распределение"),
            null,
            CancellationToken.None);
        var secondTask = CreateService(secondContext).CreateOperationAsync(
            secondFundId,
            new CreateFundOperationRequest("deposit", 80m, "Второе параллельное распределение"),
            null,
            CancellationToken.None);

        var bothCommandsWaitedForAllocationLock = false;
        try
        {
            bothCommandsWaitedForAllocationLock = await WaitForBlockedAllocationCommandsAsync(database.ConnectionString);
        }
        finally
        {
            await ExecuteLockCommandAsync(blockerConnection, "SELECT pg_advisory_unlock(@lock_key)");
        }

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.True(bothCommandsWaitedForAllocationLock);
        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.ErrorCode == "fund_distribution_amount_exceeded");
        await using var verificationContext = database.CreateContext();
        Assert.Equal(80m, await verificationContext.Funds.SumAsync(fund => fund.Balance));
        Assert.Equal(80m, await verificationContext.FundOperations
            .Where(operation => !operation.IsCanceled)
            .SumAsync(operation => operation.Amount));
        Assert.Single(verificationContext.AuditEvents, item => item.Action == "fund.operation_deposited");
    }

    private static FundService CreateService(GarageBalanceDbContext context) =>
        new(
            new EfFundRepository(context),
            new EfFinanceAvailableBalanceQuery(context),
            new AuditEventWriter(context));

    private static async Task SeedIncomeAsync(GarageBalanceDbContext context, decimal amount)
    {
        var garage = new Garage { Number = $"FUND-PG-{Guid.NewGuid():N}", PeopleCount = 1, FloorCount = 1 };
        var incomeType = new IncomeType { Name = $"Поступление {Guid.NewGuid():N}" };
        context.FinancialOperations.Add(new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            Garage = garage,
            IncomeType = incomeType,
            OperationDate = new DateOnly(2026, 7, 19),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = amount
        });
        await context.SaveChangesAsync();
    }

    private static async Task<bool> WaitForBlockedAllocationCommandsAsync(string connectionString)
    {
        await using var observerConnection = new NpgsqlConnection(connectionString);
        await observerConnection.OpenAsync();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = observerConnection.CreateCommand();
            command.CommandText = """
                SELECT count(*)
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND NOT granted
                """;
            var blockedCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (blockedCount >= 2)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static async Task ExecuteLockCommandAsync(NpgsqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("lock_key", FundAllocationLockKey);
        await command.ExecuteNonQueryAsync();
    }
}
