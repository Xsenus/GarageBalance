using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Dictionaries;
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

    [PostgreSqlFact]
    public async Task CreatedAndRenamedFundsPersistWithoutRecreatingSystemFund()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid renamedFundId;
        Guid createdFundId;
        var actorUserId = Guid.NewGuid();
        await using (var writeContext = database.CreateContext())
        {
            var service = CreateService(writeContext);
            var funds = await service.GetFundsAsync(CancellationToken.None);
            renamedFundId = funds.Single(item => item.Name == "Электроэнергия").Id;
            var incomeType = new IncomeType
            {
                Name = "Поступления за электроэнергию",
                DestinationFundId = renamedFundId
            };
            writeContext.AddRange(
                incomeType,
                new ChargeServiceSetting
                {
                    Name = "Электроэнергия по счётчику",
                    IncomeType = incomeType
                });
            await writeContext.SaveChangesAsync();

            var created = await service.CreateFundAsync(
                new UpsertFundRequest("Резервный фонд"),
                actorUserId,
                CancellationToken.None);
            var renamed = await service.UpdateFundAsync(
                renamedFundId,
                new UpsertFundRequest("Энергоснабжение"),
                actorUserId,
                CancellationToken.None);

            Assert.True(created.Succeeded, created.ErrorMessage);
            Assert.True(renamed.Succeeded, renamed.ErrorMessage);
            createdFundId = created.Value!.Id;
        }

        await using var verificationContext = database.CreateContext();
        var verificationService = CreateService(verificationContext);
        var reloaded = await verificationService.GetFundsAsync(CancellationToken.None);

        Assert.Equal(8, reloaded.Count);
        Assert.Contains(reloaded, item => item.Id == createdFundId && item.Name == "Резервный фонд" && !item.IsSystem);
        var renamedFund = Assert.Single(reloaded, item => item.Id == renamedFundId && item.Name == "Энергоснабжение" && item.IsSystem);
        Assert.Collection(
            renamedFund.LinkedServices,
            linkedService => Assert.Equal("Электроэнергия по счётчику", linkedService.Name));
        Assert.Empty(reloaded.Single(item => item.Id == createdFundId).LinkedServices);
        Assert.DoesNotContain(reloaded, item => item.Name == "Электроэнергия");
        Assert.Single(verificationContext.AuditEvents, item => item.Action == "fund.created" && item.ActorUserId == actorUserId);
        Assert.Single(verificationContext.AuditEvents, item => item.Action == "fund.updated" && item.ActorUserId == actorUserId);
    }

    [PostgreSqlFact]
    public async Task ConcurrentFundDeletionAndServiceCreation_PreserveActiveFundInvariant()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid fundId;
        Guid incomeTypeId;
        Guid tariffId;
        await using (var setupContext = database.CreateContext())
        {
            var fundService = CreateService(setupContext);
            await fundService.GetFundsAsync(CancellationToken.None);
            var created = await fundService.CreateFundAsync(
                new UpsertFundRequest("Фонд охраны"),
                null,
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
            fundId = created.Value!.Id;
            var incomeType = new IncomeType
            {
                Name = "Поступления за охрану",
                Code = "membership",
                DestinationFundId = fundId
            };
            var tariff = new Tariff
            {
                Name = "Охрана",
                CalculationBase = "fixed",
                Rate = 100m,
                EffectiveFrom = new DateOnly(2026, 7, 1)
            };
            setupContext.AddRange(incomeType, tariff);
            await setupContext.SaveChangesAsync();
            incomeTypeId = incomeType.Id;
            tariffId = tariff.Id;
        }

        await using var blockerConnection = new NpgsqlConnection(database.ConnectionString);
        await blockerConnection.OpenAsync();
        await ExecuteLockCommandAsync(blockerConnection, "SELECT pg_advisory_lock(@lock_key)");

        await using var deleteContext = database.CreateContext();
        await using var createContext = database.CreateContext();
        var deleteTask = CreateService(deleteContext).DeleteFundAsync(
            fundId,
            new DeleteFundRequest("Услуги отсутствуют"),
            null,
            CancellationToken.None);
        var createTask = DictionaryServiceTestFactory.Create(createContext).CreateChargeServiceSettingAsync(
            new UpsertChargeServiceSettingRequest(
                "Охрана территории",
                true,
                1,
                1,
                30,
                null,
                30,
                false,
                false,
                "руб.",
                incomeTypeId,
                tariffId),
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

        var deleteResult = await deleteTask;
        var createResult = await createTask;

        Assert.True(bothCommandsWaitedForAllocationLock);
        Assert.NotEqual(deleteResult.Succeeded, createResult.Succeeded);
        await using var verificationContext = database.CreateContext();
        var storedFund = await verificationContext.Funds.SingleAsync(fund => fund.Id == fundId);
        var storedIncomeType = await verificationContext.IncomeTypes.SingleAsync(item => item.Id == incomeTypeId);
        var storedService = await verificationContext.ChargeServiceSettings
            .SingleOrDefaultAsync(item => item.Name == "Охрана территории");
        if (storedFund.IsArchived)
        {
            Assert.True(deleteResult.Succeeded);
            Assert.Null(storedIncomeType.DestinationFundId);
            Assert.Null(storedService);
        }
        else
        {
            Assert.True(createResult.Succeeded);
            Assert.Equal("fund_has_linked_services", deleteResult.ErrorCode);
            Assert.Equal(fundId, storedIncomeType.DestinationFundId);
            Assert.NotNull(storedService);
        }
    }

    private static FundService CreateService(GarageBalanceDbContext context) =>
        new(
            new EfFundRepository(context),
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
