using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.ShowcaseSeed;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlPaymentAllocationIntegrationTests
{
    [PostgreSqlFact]
    public async Task FullPaymentQuote_AppliesExcessAllocationAfterAccrualReductionOnPostgreSql()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage
        {
            Number = "PG-FULL-PAYMENT-EXCESS",
            PeopleCount = 1,
            FloorCount = 1
        };
        var incomeType = new IncomeType { Name = "PG-FULL-PAYMENT-EXCESS" };
        var overpaidAccrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 7, 1),
            DueDate = new DateOnly(2026, 8, 20),
            OverdueFromDate = new DateOnly(2026, 9, 20),
            Amount = 93.22m,
            Source = AccrualSources.Manual
        };
        var outstandingAccrual = new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 9, 20),
            OverdueFromDate = new DateOnly(2026, 10, 21),
            Amount = 100m,
            Source = AccrualSources.Manual
        };
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            Garage = garage,
            IncomeType = incomeType,
            OperationDate = new DateOnly(2026, 7, 15),
            AccountingMonth = new DateOnly(2026, 7, 1),
            Amount = 100m
        };
        context.AddRange(
            overpaidAccrual,
            outstandingAccrual,
            payment,
            new AccrualPaymentAllocation
            {
                Accrual = overpaidAccrual,
                FinancialOperation = payment,
                Amount = 100m
            });
        await context.SaveChangesAsync();

        var result = await FinanceServiceTestFactory.Create(context)
            .GetGarageFullPaymentQuoteAsync(garage.Id, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(93.22m, result.Value!.TotalAmount);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(93.22m, line.OutstandingAmount);
        Assert.Equal(outstandingAccrual.AccountingMonth, line.AccountingMonth);
    }

    [PostgreSqlFact]
    public async Task OperationsPage_PreservesOpeningBalanceAndSameDayPaymentSequence()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = new Garage
        {
            Number = "PG-OPENING-HISTORY",
            PeopleCount = 1,
            FloorCount = 1,
            StartingBalance = 1_000m
        };
        var incomeType = new IncomeType { Name = "PG-OPENING-HISTORY" };
        context.AddRange(
            garage,
            incomeType,
            new Accrual
            {
                Garage = garage,
                IncomeType = incomeType,
                AccountingMonth = new DateOnly(2026, 8, 1),
                DueDate = new DateOnly(2026, 8, 31),
                OverdueFromDate = new DateOnly(2026, 9, 1),
                Amount = 200m,
                Source = AccrualSources.Manual
            });
        await context.SaveChangesAsync();

        var service = FinanceServiceTestFactory.Create(context);
        foreach (var amount in new[] { 300m, 400m })
        {
            var created = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    garage.Id,
                    incomeType.Id,
                    new DateOnly(2026, 8, 26),
                    new DateOnly(2026, 8, 1),
                    amount,
                    null,
                    "Проверка истории одного дня"),
                null,
                CancellationToken.None);
            Assert.True(created.Succeeded, created.ErrorMessage);
        }

        var page = await service.GetOperationsPageAsync(
            new FinancialOperationListRequest(
                null,
                null,
                FinancialOperationKinds.Income,
                null,
                Limit: 100,
                GarageId: garage.Id),
            CancellationToken.None);
        var ordered = page.Items.OrderBy(operation => operation.CreatedAtUtc).ToArray();

        Assert.Equal(2, ordered.Length);
        Assert.Equal(1_200m, ordered[0].GarageDebtBefore);
        Assert.Equal(900m, ordered[0].GarageDebtAfter);
        Assert.Equal(900m, ordered[1].GarageDebtBefore);
        Assert.Equal(500m, ordered[1].GarageDebtAfter);
    }

    [PostgreSqlFact]
    public async Task GarageWorksheetLock_SerializesSameGarageWithoutBlockingAnotherGarage()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var ownerContext = database.CreateContext();
        await using var independentContext = database.CreateContext();
        await using var waiterContext = database.CreateContext();
        var garageId = Guid.NewGuid();
        var ownerRepository = new EfAccrualPaymentAllocationRepository(ownerContext);
        var independentRepository = new EfAccrualPaymentAllocationRepository(independentContext);
        var waiterRepository = new EfAccrualPaymentAllocationRepository(waiterContext);
        var ownerLease = await ownerRepository.AcquireGarageIncomeWorksheetLockAsync(
            garageId,
            CancellationToken.None);

        await using var independentLease = await independentRepository
            .AcquireGarageIncomeWorksheetLockAsync(Guid.NewGuid(), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        var waiterTask = waiterRepository.AcquireGarageIncomeWorksheetLockAsync(
            garageId,
            CancellationToken.None);
        await WaitForAdvisoryLockWaitersAsync(ownerContext, expectedCount: 1);
        Assert.False(waiterTask.IsCompleted);

        await ownerLease.DisposeAsync();
        await using var waiterLease = await waiterTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [PostgreSqlFact]
    public async Task AllocationRebuild_ReplacesExistingActivePairWithoutUniqueViolation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(database, "PG-REBUILD-EXISTING", [100m]);

        await using (var paymentContext = database.CreateContext())
        {
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                CreateConcurrentPayment(ledger, "PG-REBUILD-EXISTING-1") with { Amount = 50m },
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
        }

        await using (var rebuildContext = database.CreateContext())
        {
            var repository = new EfAccrualPaymentAllocationRepository(rebuildContext);
            await repository.RebuildAsync(
                [new AccrualPaymentAllocationKey(ledger.GarageId, ledger.IncomeTypeId)],
                CancellationToken.None);
            await rebuildContext.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        var active = await assertionContext.AccrualPaymentAllocations
            .Where(item => item.IsActive && item.AccrualId == ledger.AccrualIds[0])
            .ToArrayAsync();
        Assert.Single(active);
        Assert.Equal(50m, active[0].Amount);
        Assert.True(await assertionContext.AccrualPaymentAllocations
            .AnyAsync(item => !item.IsActive && item.AccrualId == ledger.AccrualIds[0]));
    }

    [PostgreSqlFact]
    public async Task ShowcaseGarage103_JulyAugustWorksheetRecalculatesWithoutPersistenceConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var seedResult = await new ShowcaseDataSeeder(seedContext).PrepareAsync(CancellationToken.None);
            Assert.True(seedResult.IsReady);
            garageId = await seedContext.Garages
                .Where(item => item.Number == "103-ДОЛЖНИК")
                .Select(item => item.Id)
                .SingleAsync();
        }

        await using var calculationContext = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(calculationContext)
            .CalculateGarageIncomeWorksheetAsync(
                garageId,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1)),
                null,
                CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotEmpty(result.Value!.Rows);
    }

    [PostgreSqlFact]
    public async Task ShowcaseGarage103_AugustWorksheetReusesSeededRegularAccruals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var seedResult = await new ShowcaseDataSeeder(seedContext).PrepareAsync(CancellationToken.None);
            Assert.True(seedResult.IsReady);
            garageId = await seedContext.Garages
                .Where(item => item.Number == "103-ДОЛЖНИК")
                .Select(item => item.Id)
                .SingleAsync();
        }

        await using var calculationContext = database.CreateContext();
        var result = await FinanceServiceTestFactory.Create(calculationContext)
            .CalculateGarageIncomeWorksheetAsync(
                garageId,
                new GarageIncomeWorksheetRequest(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)),
                null,
                CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotEmpty(result.Value!.Rows);

        await using var assertionContext = database.CreateContext();
        var annualAccruals = await assertionContext.Accruals
            .Where(item =>
                !item.IsCanceled &&
                item.GarageId == garageId &&
                item.Source == AccrualSources.Regular &&
                item.AccountingYear == 2026)
            .Select(item => item.IncomeType.Code!)
            .OrderBy(item => item)
            .ToArrayAsync();
        Assert.Equal(["membership", "outdoor_lighting", "target"], annualAccruals);
    }

    [PostgreSqlFact]
    public async Task ShowcaseGarage103_ConcurrentWorksheetCalculationsAreSerializedBeforeReadingAccruals()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        AccrualPaymentAllocationKey[] allocationKeys;
        await using (var seedContext = database.CreateContext())
        {
            var seedResult = await new ShowcaseDataSeeder(seedContext).PrepareAsync(CancellationToken.None);
            Assert.True(seedResult.IsReady);
            garageId = await seedContext.Garages
                .Where(item => item.Number == "103-ДОЛЖНИК")
                .Select(item => item.Id)
                .SingleAsync();
            var incomeTypeIds = await seedContext.ChargeServiceSettings
                .Where(item => !item.IsArchived && item.IsRegular && item.IncomeTypeId.HasValue)
                .Select(item => item.IncomeTypeId!.Value)
                .Distinct()
                .ToArrayAsync();
            allocationKeys = incomeTypeIds
                .Select(incomeTypeId => new AccrualPaymentAllocationKey(garageId, incomeTypeId))
                .ToArray();

            var regularAccrualIds = await seedContext.Accruals
                .Where(item => item.GarageId == garageId && item.Source == AccrualSources.Regular)
                .Select(item => item.Id)
                .ToArrayAsync();
            await seedContext.AccrualPaymentAllocations
                .Where(item => regularAccrualIds.Contains(item.AccrualId))
                .ExecuteDeleteAsync();
            await seedContext.Accruals
                .Where(item => regularAccrualIds.Contains(item.Id))
                .ExecuteDeleteAsync();
        }

        await using var blockerContext = database.CreateContext();
        var blockerRepository = new EfAccrualPaymentAllocationRepository(blockerContext);
        var blockerLease = await blockerRepository.AcquireRebuildLockAsync(allocationKeys, CancellationToken.None);
        try
        {
            await using var firstContext = database.CreateContext();
            await using var secondContext = database.CreateContext();
            var request = new GarageIncomeWorksheetRequest(
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 8, 1));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Task.Run(async () =>
            {
                await start.Task;
                return await FinanceServiceTestFactory.Create(firstContext)
                    .CalculateGarageIncomeWorksheetAsync(garageId, request, null, CancellationToken.None);
            });
            var second = Task.Run(async () =>
            {
                await start.Task;
                return await FinanceServiceTestFactory.Create(secondContext)
                    .CalculateGarageIncomeWorksheetAsync(garageId, request, null, CancellationToken.None);
            });

            start.SetResult();
            await WaitForAdvisoryLockWaitersAsync(blockerContext, expectedCount: 2);
            await blockerLease.DisposeAsync();
            blockerLease = null;

            var results = await Task.WhenAll(first, second);
            Assert.All(results, result => Assert.True(result.Succeeded, result.ErrorMessage));
            Assert.All(results, result => Assert.NotEmpty(result.Value!.Rows));
        }
        finally
        {
            if (blockerLease is not null)
            {
                await blockerLease.DisposeAsync();
            }
        }
    }

    [PostgreSqlFact]
    public async Task WorksheetAndBatchGeneration_ReadAccrualsAfterTheirSharedGarageLock()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid worksheetGarageId;
        Guid secondGarageId;
        Guid incomeTypeId;
        Guid tariffId;
        var month = new DateOnly(2026, 9, 1);
        await using (var seedContext = database.CreateContext())
        {
            var firstGarage = new Garage
            {
                Number = "PG-WORKSHEET-RACE-1",
                PeopleCount = 1,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var secondGarage = new Garage
            {
                Number = "PG-WORKSHEET-RACE-2",
                PeopleCount = 1,
                CreatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            };
            var incomeType = new IncomeType { Name = "PG-WORKSHEET-RACE" };
            var tariff = new Tariff
            {
                Name = "PG-WORKSHEET-RACE",
                CalculationBase = TariffCalculationBases.Fixed,
                Rate = 100m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            };
            var setting = new ChargeServiceSetting
            {
                Name = "PG-WORKSHEET-RACE",
                IsRegular = true,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                OverdueGraceDays = 30,
                IncomeType = incomeType,
                Tariff = tariff
            };
            seedContext.AddRange(firstGarage, secondGarage, setting);
            await seedContext.SaveChangesAsync();
            worksheetGarageId = firstGarage.Id;
            secondGarageId = secondGarage.Id;
            incomeTypeId = incomeType.Id;
            tariffId = tariff.Id;
        }

        await using var blockerContext = database.CreateContext();
        var blockerRepository = new EfAccrualPaymentAllocationRepository(blockerContext);
        var blockerLease = await blockerRepository.AcquireRebuildLockAsync(
            [new AccrualPaymentAllocationKey(worksheetGarageId, incomeTypeId)],
            CancellationToken.None);
        try
        {
            await using var worksheetContext = database.CreateContext();
            var worksheetTask = FinanceServiceTestFactory.Create(worksheetContext)
                .CalculateGarageIncomeWorksheetAsync(
                    worksheetGarageId,
                    new GarageIncomeWorksheetRequest(month, month),
                    null,
                    CancellationToken.None);
            await WaitForAdvisoryLockWaitersAsync(blockerContext, expectedCount: 1);

            await using var generationContext = database.CreateContext();
            var generationTask = FinanceServiceTestFactory.Create(generationContext)
                .GenerateRegularAccrualsAsync(
                    new GenerateRegularAccrualsRequest(incomeTypeId, tariffId, month, "Проверка общей блокировки"),
                    null,
                    CancellationToken.None);
            await WaitForGenerationCompletionOrAdvisoryLockWaitersAsync(
                blockerContext,
                generationTask,
                expectedCount: 2);

            await blockerLease.DisposeAsync();
            blockerLease = null;

            var worksheet = await worksheetTask;
            var generation = await generationTask;
            Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
            Assert.True(generation.Succeeded, generation.ErrorMessage);
        }
        finally
        {
            if (blockerLease is not null)
            {
                await blockerLease.DisposeAsync();
            }
        }

        await using var assertionContext = database.CreateContext();
        var activeAccruals = await assertionContext.Accruals
            .Where(item =>
                !item.IsCanceled &&
                item.IncomeTypeId == incomeTypeId &&
                item.AccountingMonth == month &&
                item.Source == AccrualSources.Regular)
            .OrderBy(item => item.GarageId)
            .ToArrayAsync();
        Assert.Equal(2, activeAccruals.Length);
        Assert.Single(activeAccruals, item => item.GarageId == worksheetGarageId);
        Assert.Single(activeAccruals, item => item.GarageId == secondGarageId);
    }

    private static async Task WaitForAdvisoryLockWaitersAsync(
        GarageBalanceDbContext context,
        int expectedCount)
    {
        var connection = context.Database.GetDbConnection();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT count(*)::int
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND NOT granted
                """;
            var waitingCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waitingCount >= expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Expected {expectedCount} concurrent advisory-lock waiters.");
    }

    private static async Task WaitForGenerationCompletionOrAdvisoryLockWaitersAsync(
        GarageBalanceDbContext context,
        Task generationTask,
        int expectedCount)
    {
        var connection = context.Database.GetDbConnection();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (generationTask.IsCompleted)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT count(*)::int
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND NOT granted
                """;
            var waitingCount = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waitingCount >= expectedCount)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Expected generation completion or {expectedCount} advisory-lock waiters.");
    }

    [PostgreSqlFact]
    public async Task AprilTrashPayment_PersistsAndRemovesGarage101OverdueDebtAfterReload()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(
            database,
            "101",
            [360m],
            new DateOnly(2026, 4, 1),
            "Мусор");

        await using (var paymentContext = database.CreateContext())
        {
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    ledger.GarageId,
                    ledger.IncomeTypeId,
                    new DateOnly(2026, 7, 28),
                    new DateOnly(2026, 4, 1),
                    360m,
                    "GARAGE-101-TRASH-APRIL",
                    "Синтетическая проверка платежа за вывоз мусора"),
                null,
                CancellationToken.None);

            Assert.True(payment.Succeeded, payment.ErrorMessage);
            Assert.Equal(0m, payment.Value!.GarageDebtAfter);
        }

        await using var reloadedContext = database.CreateContext();
        var persistedPayment = await reloadedContext.FinancialOperations
            .SingleAsync(item => item.DocumentNumber == "GARAGE-101-TRASH-APRIL");
        var allocation = await reloadedContext.AccrualPaymentAllocations
            .SingleAsync(item => item.IsActive && item.FinancialOperationId == persistedPayment.Id);
        var overdue = await FinanceServiceTestFactory.Create(reloadedContext)
            .GetGarageOverdueDebtAsync(ledger.GarageId, CancellationToken.None);

        Assert.Equal(360m, persistedPayment.Amount);
        Assert.Equal(360m, allocation.Amount);
        Assert.True(overdue.Succeeded, overdue.ErrorMessage);
        Assert.Equal(0m, overdue.Value!.Total);
        Assert.Empty(overdue.Value.Rows);
    }

    [PostgreSqlFact]
    public async Task PeriodAndLegacyPayments_PersistRegularPriorityAndThenCloseIrregularDebt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(database, "PG-TARGET-PRIORITY", [3_000m, 300m]);
        await using (var setupContext = database.CreateContext())
        {
            var irregularPayment = new IrregularPayment
            {
                Name = "Внеочередной вывоз мусора",
                Amount = 3_000m,
                IsActive = true
            };
            setupContext.IrregularPayments.Add(irregularPayment);
            await setupContext.SaveChangesAsync();
            var targetedAccrual = await setupContext.Accruals.SingleAsync(item => item.Id == ledger.AccrualIds[0]);
            targetedAccrual.IrregularPaymentId = irregularPayment.Id;
            await setupContext.SaveChangesAsync();
        }

        Guid periodPaymentId;
        Guid legacyPaymentId;
        await using (var paymentContext = database.CreateContext())
        {
            var service = FinanceServiceTestFactory.Create(paymentContext);
            var periodPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    ledger.GarageId,
                    ledger.IncomeTypeId,
                    new DateOnly(2026, 2, 20),
                    new DateOnly(2026, 2, 1),
                    300m,
                    "PG-PERIOD-300",
                    "Оплата регулярной услуги за период"),
                null,
                CancellationToken.None);
            Assert.True(periodPayment.Succeeded, periodPayment.ErrorMessage);
            periodPaymentId = periodPayment.Value!.Id;

            var legacyPayment = await service.CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    ledger.GarageId,
                    ledger.IncomeTypeId,
                    new DateOnly(2026, 2, 21),
                    new DateOnly(2026, 1, 1),
                    3_000m,
                    "PG-LEGACY-IRREGULAR-3000",
                    "Историческая оплата без явной привязки"),
                null,
                CancellationToken.None);
            Assert.True(legacyPayment.Succeeded, legacyPayment.ErrorMessage);
            legacyPaymentId = legacyPayment.Value!.Id;
        }

        await using var assertionContext = database.CreateContext();
        var allocations = await assertionContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && (item.FinancialOperationId == periodPaymentId || item.FinancialOperationId == legacyPaymentId))
            .ToDictionaryAsync(item => item.FinancialOperationId);

        Assert.Equal(ledger.AccrualIds[1], allocations[periodPaymentId].AccrualId);
        Assert.Equal(300m, allocations[periodPaymentId].Amount);
        Assert.Equal(ledger.AccrualIds[0], allocations[legacyPaymentId].AccrualId);
        Assert.Equal(3_000m, allocations[legacyPaymentId].Amount);
    }

    [PostgreSqlFact]
    public async Task PeriodPayment_PersistsSelectedMonthBeforeOlderOrdinaryDebt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(database, "PG-SELECTED-MONTH", [300m, 300m]);
        Guid paymentId;
        await using (var paymentContext = database.CreateContext())
        {
            var payment = await FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                new CreateIncomeOperationRequest(
                    ledger.GarageId,
                    ledger.IncomeTypeId,
                    new DateOnly(2026, 2, 20),
                    new DateOnly(2026, 2, 1),
                    100m,
                    "PG-SELECTED-MONTH-100",
                    "Оплата за выбранный средний месяц"),
                null,
                CancellationToken.None);
            Assert.True(payment.Succeeded, payment.ErrorMessage);
            paymentId = payment.Value!.Id;
        }

        await using var assertionContext = database.CreateContext();
        var allocation = await assertionContext.AccrualPaymentAllocations
            .AsNoTracking()
            .SingleAsync(item => item.IsActive && item.FinancialOperationId == paymentId);

        Assert.Equal(ledger.AccrualIds[1], allocation.AccrualId);
        Assert.Equal(100m, allocation.Amount);
    }

    [PostgreSqlFact]
    public async Task PrincipalMigration_RebuildsLegacyFifoAllocationToSelectedAccountingMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            "20260831014500_OptimizeMeterReadingYearGrid");
        var ledger = await SeedLedgerAsync(
            database,
            "PG-SELECTED-MONTH-MIGRATION",
            [300m, 300m, 300m],
            new DateOnly(2026, 6, 1));
        Guid paymentId;
        Guid legacyAllocationId;
        await using (var legacyContext = database.CreateContext())
        {
            var payment = new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = new DateOnly(2026, 7, 20),
                AccountingMonth = new DateOnly(2026, 7, 1),
                Amount = 100m,
                GarageId = ledger.GarageId,
                IncomeTypeId = ledger.IncomeTypeId,
                DocumentNumber = "PG-LEGACY-FIFO-BEFORE-MIGRATION"
            };
            var legacyAllocation = new AccrualPaymentAllocation
            {
                FinancialOperation = payment,
                // Mirrors the deployed anomaly: a July payment was historically
                // attached to August while the July principal remained unpaid.
                AccrualId = ledger.AccrualIds[2],
                Amount = 100m
            };
            legacyContext.AddRange(payment, legacyAllocation);
            await legacyContext.SaveChangesAsync();
            paymentId = payment.Id;
            legacyAllocationId = legacyAllocation.Id;
        }

        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        Assert.False((await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .SingleAsync(item => item.Id == legacyAllocationId)).IsActive);
        var repairedAllocation = Assert.Single(await verificationContext.AccrualPaymentAllocations
            .AsNoTracking()
            .Where(item => item.IsActive && item.FinancialOperationId == paymentId)
            .ToArrayAsync());
        Assert.Equal(ledger.AccrualIds[1], repairedAllocation.AccrualId);
        Assert.Equal(100m, repairedAllocation.Amount);
        var preservedPayment = await verificationContext.FinancialOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == paymentId);
        Assert.Equal(new DateOnly(2026, 7, 1), preservedPayment.AccountingMonth);
        Assert.Equal(100m, preservedPayment.Amount);
        var preservedAccruals = await verificationContext.Accruals
            .AsNoTracking()
            .Where(item => ledger.AccrualIds.Contains(item.Id))
            .OrderBy(item => item.AccountingMonth)
            .ToArrayAsync();
        Assert.Equal(3, preservedAccruals.Length);
        Assert.All(preservedAccruals, item =>
        {
            Assert.False(item.IsCanceled);
            Assert.Equal(300m, item.Amount);
        });
    }

    [PostgreSqlFact]
    public async Task IncomeWorksheet_CapsEveryAccrualAndShowsUnallocatedRemainderAsAdvance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(database, "PG-ADVANCE", [60m, 70m]);
        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);

        var payment = await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(
                ledger.GarageId,
                ledger.IncomeTypeId,
                new DateOnly(2026, 3, 10),
                new DateOnly(2026, 3, 1),
                150m,
                "PG-ADVANCE-1",
                null),
            null,
            CancellationToken.None);
        Assert.True(payment.Succeeded, payment.ErrorMessage);

        var worksheet = await service.GetGarageIncomeWorksheetAsync(
            ledger.GarageId,
            new GarageIncomeWorksheetRequest(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1)),
            CancellationToken.None);

        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        var serviceRows = worksheet.Value!.Rows
            .Where(row => row.IncomeTypeId == ledger.IncomeTypeId)
            .OrderBy(row => row.AccountingMonth)
            .ToArray();
        Assert.Equal(3, serviceRows.Length);
        Assert.Equal((60m, 0m), (serviceRows[0].IncomeAmount, serviceRows[0].AdvanceAmount));
        Assert.Equal((70m, 0m), (serviceRows[1].IncomeAmount, serviceRows[1].AdvanceAmount));
        Assert.Equal((0m, 20m), (serviceRows[2].IncomeAmount, serviceRows[2].AdvanceAmount));
        Assert.Equal(20m, worksheet.Value.AdvanceTotal);
        Assert.All(serviceRows, row => Assert.True(row.IncomeAmount <= row.PayableAmount));
    }

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

    [PostgreSqlFact]
    public async Task AllocationRebuild_SerializesCreateUpdateCancelAndRestoreEntrypoints()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ledger = await SeedLedgerAsync(database, "PG-ALLOCATION-ENTRYPOINTS", [100m, 100m]);
        await using (var initialPaymentContext = database.CreateContext())
        {
            var result = await FinanceServiceTestFactory.Create(initialPaymentContext).CreateIncomeAsync(
                CreateConcurrentPayment(ledger, "PG-ALLOCATION-INITIAL") with { Amount = 100m },
                null,
                CancellationToken.None);
            Assert.True(result.Succeeded, result.ErrorMessage);
        }

        await using (var cancelContext = database.CreateContext())
        await using (var paymentContext = database.CreateContext())
        {
            var results = await Task.WhenAll(
                AsSucceeded(FinanceServiceTestFactory.Create(cancelContext).CancelAccrualAsync(
                    ledger.AccrualIds[0],
                    new CancelFinanceEntryRequest("Конкурентная отмена начисления"),
                    null,
                    CancellationToken.None)),
                AsSucceeded(FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                    CreateConcurrentPayment(ledger, "PG-ALLOCATION-CANCEL-RACE"),
                    null,
                    CancellationToken.None)));
            Assert.All(results, Assert.True);
        }
        await AssertAllocationInvariantAsync(database, ledger, expectedActiveAccrualTotal: 100m);

        await using (var restoreContext = database.CreateContext())
        await using (var paymentContext = database.CreateContext())
        {
            var results = await Task.WhenAll(
                AsSucceeded(FinanceServiceTestFactory.Create(restoreContext).RestoreAccrualAsync(
                    ledger.AccrualIds[0],
                    null,
                    CancellationToken.None)),
                AsSucceeded(FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                    CreateConcurrentPayment(ledger, "PG-ALLOCATION-RESTORE-RACE"),
                    null,
                    CancellationToken.None)));
            Assert.All(results, Assert.True);
        }
        await AssertAllocationInvariantAsync(database, ledger, expectedActiveAccrualTotal: 200m);

        await using (var updateContext = database.CreateContext())
        await using (var paymentContext = database.CreateContext())
        {
            var results = await Task.WhenAll(
                AsSucceeded(FinanceServiceTestFactory.Create(updateContext).UpdateAccrualAsync(
                    ledger.AccrualIds[1],
                    new CreateAccrualRequest(
                        ledger.GarageId,
                        ledger.IncomeTypeId,
                        new DateOnly(2026, 2, 1),
                        50m,
                        AccrualSources.Manual,
                        "Конкурентное изменение начисления"),
                    null,
                    CancellationToken.None)),
                AsSucceeded(FinanceServiceTestFactory.Create(paymentContext).CreateIncomeAsync(
                    CreateConcurrentPayment(ledger, "PG-ALLOCATION-UPDATE-RACE"),
                    null,
                    CancellationToken.None)));
            Assert.All(results, Assert.True);
        }
        await AssertAllocationInvariantAsync(database, ledger, expectedActiveAccrualTotal: 150m);
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

    private static async Task<bool> AsSucceeded<T>(Task<FinanceResult<T>> task) => (await task).Succeeded;

    private static async Task AssertAllocationInvariantAsync(
        PostgreSqlTestDatabase database,
        SeededLedger ledger,
        decimal expectedActiveAccrualTotal)
    {
        await using var context = database.CreateContext();
        var activeAccruals = await context.Accruals
            .Where(accrual => ledger.AccrualIds.Contains(accrual.Id) && !accrual.IsCanceled)
            .Select(accrual => new { accrual.Id, accrual.Amount })
            .ToArrayAsync();
        var activeAllocations = await context.AccrualPaymentAllocations
            .Where(allocation => allocation.IsActive && ledger.AccrualIds.Contains(allocation.AccrualId))
            .GroupBy(allocation => allocation.AccrualId)
            .Select(group => new { AccrualId = group.Key, Amount = group.Sum(allocation => allocation.Amount) })
            .ToArrayAsync();

        Assert.Equal(expectedActiveAccrualTotal, activeAccruals.Sum(accrual => accrual.Amount));
        Assert.Equal(expectedActiveAccrualTotal, activeAllocations.Sum(allocation => allocation.Amount));
        Assert.All(activeAllocations, allocation =>
        {
            var accrual = Assert.Single(activeAccruals, item => item.Id == allocation.AccrualId);
            Assert.True(allocation.Amount <= accrual.Amount);
        });
        Assert.DoesNotContain(activeAllocations, allocation => activeAccruals.All(accrual => accrual.Id != allocation.AccrualId));
    }

    private static async Task<SeededLedger> SeedLedgerAsync(
        PostgreSqlTestDatabase database,
        string suffix,
        IReadOnlyList<decimal> amounts,
        DateOnly? firstAccountingMonth = null,
        string? incomeTypeName = null)
    {
        await using var context = database.CreateContext();
        var garage = new Garage { Number = suffix, PeopleCount = 1, FloorCount = 1 };
        var resolvedIncomeTypeName = incomeTypeName ?? suffix;
        var incomeType = await context.IncomeTypes
            .SingleOrDefaultAsync(item => item.Name == resolvedIncomeTypeName)
            ?? new IncomeType { Name = resolvedIncomeTypeName };
        var firstMonth = firstAccountingMonth ?? new DateOnly(2026, 1, 1);
        var accruals = amounts.Select((amount, index) => new Accrual
        {
            Garage = garage,
            IncomeType = incomeType,
            AccountingMonth = firstMonth.AddMonths(index),
            DueDate = firstMonth.AddMonths(index).AddMonths(1).AddDays(-1),
            OverdueFromDate = firstMonth.AddMonths(index + 1),
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
