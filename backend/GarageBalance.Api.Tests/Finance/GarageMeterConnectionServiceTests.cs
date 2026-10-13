using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageMeterConnectionServiceTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly ConnectedOn = September.AddDays(9);

    [Fact]
    public async Task BatchConnectsAllGaragesInStableOrderWithZeroAndPendingBase()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyBatchSuccessAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlBatchAndPreviewKeepAtomicityAndReleaseLocks()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyBatchSuccessAsync(context);
        var garage = await context.Garages.OrderBy(item => item.Id).FirstAsync();
        var connection = await context.GarageMeterConnections.SingleAsync(item => item.GarageId == garage.Id);
        var result = await CreateService(context).PreviewAsync(new(garage.Id, garage.Version,
            [new(connection.MeterKind, 20, ConnectedOn, connection.Version, "Проверка")]), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(0m, (await context.MeterDevices.SingleAsync()).InitialValue);
    }

    private static async Task VerifyBatchSuccessAsync(GarageBalanceDbContext context)
    {
        var (garages, setting) = await SeedAsync(context);
        var result = await CreateService(context).ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn,
            [new(garages[1].Id, null), new(garages[0].Id, 0)]), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal([garages[0].Id, garages[1].Id], result.Value!.Items.Select(item => item.GarageId));
        Assert.All(result.Value.Items, row => Assert.Equal(ConnectedOn, Assert.Single(row.Changes).NewConnectedOn));
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.GarageMeterConnections.CountAsync());
        Assert.Equal(0m, (await context.MeterDevices.SingleAsync()).InitialValue);
        Assert.Null((await context.GarageMeterConnections.SingleAsync(item => item.GarageId == garages[1].Id)).BaselineMeterDeviceId);
        Assert.Equal(2, await context.AuditEvents.CountAsync(item => item.Action == "finance.meter_connection_changed"));
        Assert.All(await context.Garages.ToListAsync(), garage => Assert.Equal(50, garage.InitialWaterMeterValue));
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("invalid-scope")]
    [InlineData("existing")]
    [InlineData("closed")]
    public async Task BatchFailureAfterFirstGarageDiscardsEveryPendingChange(string failure)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garages, setting) = await SeedAsync(context);
        if (failure == "invalid-scope") setting.GarageIds = [garages[0].Id];
        else context.GarageMeterConnections.Add(new GarageMeterConnection
        {
            Garage = garages[1],
            MeterKind = setting.MeterKind!,
            ConnectedOn = ConnectedOn,
            ClosedOn = failure == "closed" ? ConnectedOn : null
        });
        await context.SaveChangesAsync();
        var before = await context.GarageMeterConnections.CountAsync();
        var result = await CreateService(context).ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn,
            [new(garages[1].Id, 25), new(garages[0].Id, 10)]), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(failure == "invalid-scope" ? "meter_start_service_not_applicable" : "meter_connection_already_exists", result.ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(before, await context.GarageMeterConnections.CountAsync());
        Assert.Empty(await context.MeterDevices.ToListAsync());
        Assert.Empty(await context.AuditEvents.ToListAsync());
        await context.SaveChangesAsync();
        Assert.False(await context.GarageMeterConnections.AnyAsync(item => item.GarageId == garages[0].Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchRollsBackEvenWhenSaveOrCancellationFails(bool cancel)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garages, setting) = await SeedAsync(context);
        using var cancellation = new CancellationTokenSource();
        var baseline = new CancelingBaseline(FinanceServiceTestFactory.Create(context), cancel ? cancellation : null);
        var service = CreateService(context, baseline, cancel ? null : new FailingAfterSave(context));
        var request = new ConnectGarageMetersBatchRequest(setting.MeterKind!, ConnectedOn,
            [new(garages[0].Id, 0), new(garages[1].Id, 0)]);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConnectBatchAsync(request, null, cancellation.Token));
        else Assert.Equal("meter_connection_changed", (await service.ConnectBatchAsync(request, null, CancellationToken.None)).ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(await context.GarageMeterConnections.AnyAsync());
        Assert.False(await context.MeterDevices.AnyAsync());
        Assert.False(await context.AuditEvents.AnyAsync());
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task BatchValidatesLimitsDuplicatesMissingGaragesAndFieldValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garages, setting) = await SeedAsync(database.Context);
        var valid = new ConnectGarageMetersBatchRequest(setting.MeterKind!, ConnectedOn, [new(garages[0].Id, 0)]);
        var invalid = new[]
        {
            valid with { Items = [] }, valid with { Items = null! }, valid with { Items = [null!] },
            valid with { Items = [new(Guid.Empty, 0)] }, valid with { Items = [valid.Items[0], valid.Items[0]] },
            valid with { Items = Enumerable.Range(0, 101).Select(_ => new ConnectGarageMeterItemRequest(Guid.NewGuid(), 0)).ToArray() },
            valid with { MeterKind = "invalid" }, valid with { MeterKind = null! }, valid with { ConnectedOn = DateOnly.MinValue },
            valid with { Reason = new string('x', 1001) }, valid with { Items = [new(garages[0].Id, -1)] },
            valid with { Items = [new(garages[0].Id, 1000000000)] }
        };
        foreach (var request in invalid)
            Assert.Equal("meter_connection_batch_invalid", (await CreateService(database.Context).ConnectBatchAsync(request, null, CancellationToken.None)).ErrorCode);
        Assert.Equal("garage_not_found", (await CreateService(database.Context).ConnectBatchAsync(
            valid with { Items = [new(Guid.NewGuid(), 0)] }, null, CancellationToken.None)).ErrorCode);
        Assert.False(await database.Context.GarageMeterConnections.AnyAsync());
    }

    [Fact]
    public async Task PreviewShowsRealAccrualDeltaWithoutPersistingHistoryOrAudit()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garages, setting) = await SeedAsync(context);
        var garage = garages[0];
        var service = CreateService(context);
        Assert.True((await service.ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn, [new(garage.Id, 250)]), null, CancellationToken.None)).Succeeded);
        var reading = await FinanceServiceTestFactory.Create(context, businessDate: September.AddDays(29)).CreateMeterReadingAsync(
            new(garage.Id, setting.MeterKind!, September, September.AddDays(29), 273, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var connection = await context.GarageMeterConnections.SingleAsync();
        var version = connection.Version;
        var auditCount = await context.AuditEvents.CountAsync();
        var preview = await service.PreviewAsync(new(garage.Id, garage.Version,
            [new(setting.MeterKind!, 260, ConnectedOn, version, "Уточнение стартового показания")]), null, CancellationToken.None);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        var change = Assert.Single(preview.Value!.Changes);
        Assert.Equal((250m, 260m, ConnectedOn, ConnectedOn), (change.OldValue, change.NewValue, change.OldConnectedOn, change.NewConnectedOn));
        var accrual = Assert.Single(preview.Value.AccrualChanges);
        Assert.Equal((September, 115m, 65m), (accrual.AccountingMonth, accrual.OldAmount, accrual.NewAmount));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(115m, (await context.Accruals.SingleAsync()).Amount);
        Assert.Equal(23m, (await context.MeterReadings.SingleAsync()).Consumption);
        Assert.Equal(250m, (await context.MeterDevices.SingleAsync()).InitialValue);
        Assert.Equal(version, (await context.GarageMeterConnections.SingleAsync()).Version);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
        await context.SaveChangesAsync();
        Assert.Equal(115m, (await context.Accruals.SingleAsync()).Amount);
    }

    [Fact]
    public async Task PreviewRejectsStaleVersionsInvalidDataAndPaidAccruals()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garages, setting) = await SeedAsync(context);
        var garage = garages[0];
        var service = CreateService(context);
        var request = new PreviewGarageMeterConnectionsRequest(garage.Id, garage.Version, [new(setting.MeterKind!, 250, ConnectedOn)]);
        foreach (var invalid in new[] { request with { GarageId = Guid.Empty }, request with { GarageVersion = Guid.Empty },
                     request with { Values = [] }, request with { Values = null! }, request with { Values = [null!] },
                     request with { Values = Enumerable.Repeat(request.Values[0], 501).ToArray() } })
            Assert.Equal("meter_connection_preview_invalid", (await service.PreviewAsync(invalid, null, CancellationToken.None)).ErrorCode);
        Assert.Equal("garage_not_found", (await service.PreviewAsync(request with { GarageId = Guid.NewGuid() }, null, CancellationToken.None)).ErrorCode);
        Assert.Equal("meter_connection_changed", (await service.PreviewAsync(request with { GarageVersion = Guid.NewGuid() }, null, CancellationToken.None)).ErrorCode);
        Assert.True((await service.ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn, [new(garage.Id, 250)]), null, CancellationToken.None)).Succeeded);
        var connection = await context.GarageMeterConnections.SingleAsync();
        Assert.Equal("meter_connection_changed", (await service.PreviewAsync(request with
        {
            Values = [new(setting.MeterKind!, 260, ConnectedOn, Guid.NewGuid(), "Исправление")]
        }, null, CancellationToken.None)).ErrorCode);
        var reading = await FinanceServiceTestFactory.Create(context, businessDate: September.AddDays(29)).CreateMeterReadingAsync(
            new(garage.Id, setting.MeterKind!, September, September.AddDays(29), 273, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync();
        context.AccrualPaymentAllocations.Add(new AccrualPaymentAllocation
        {
            Accrual = accrual,
            Amount = 1,
            FinancialOperation = new FinancialOperation
            {
                GarageId = garage.Id,
                IncomeTypeId = setting.IncomeTypeId,
                OperationKind = FinancialOperationKinds.Income,
                OperationDate = September,
                AccountingMonth = September,
                Amount = 1
            }
        });
        await context.SaveChangesAsync();
        Assert.Equal("meter_start_accrual_paid", (await service.PreviewAsync(request with
        {
            Values = [new(setting.MeterKind!, 260, ConnectedOn, connection.Version, "Исправление")]
        }, null, CancellationToken.None)).ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(115m, (await context.Accruals.SingleAsync()).Amount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewDiscardsEarlierValuesWhenAnotherValueFailsOrRequestIsCanceled(bool cancel)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garages, setting) = await SeedAsync(context);
        using var cancellation = new CancellationTokenSource();
        var service = CreateService(context, new CancelingBaseline(FinanceServiceTestFactory.Create(context), cancel ? cancellation : null));
        var values = new List<UpsertGarageMeterStartValueRequest> { new(setting.MeterKind!, 0, ConnectedOn) };
        if (!cancel) values.Add(new(MeterKinds.ForService(Guid.NewGuid()), 0, ConnectedOn));
        var request = new PreviewGarageMeterConnectionsRequest(garages[0].Id, garages[0].Version, values);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewAsync(request, null, cancellation.Token));
        else Assert.Equal("meter_start_service_not_applicable", (await service.PreviewAsync(request, null, cancellation.Token)).ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(await context.GarageMeterConnections.AnyAsync());
        Assert.False(await context.MeterDevices.AnyAsync());
        Assert.False(await context.AuditEvents.AnyAsync());
        Assert.Null(context.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task ConcurrentBatchesCannotOverwriteOrDuplicateAnExistingConnection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        ConnectGarageMetersBatchRequest request;
        await using (var seed = database.CreateContext())
        {
            var (garages, setting) = await SeedAsync(seed);
            request = new(setting.MeterKind!, ConnectedOn, [new(garages[0].Id, 250)]);
        }
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var results = await Task.WhenAll(CreateService(first).ConnectBatchAsync(request, null, CancellationToken.None),
            CreateService(second).ConnectBatchAsync(request with { Items = [new(request.Items[0].GarageId, 500)] }, null, CancellationToken.None));
        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded);
        Assert.Equal(1, await first.GarageMeterConnections.CountAsync());
        Assert.Equal(1, await first.MeterDevices.CountAsync());
        Assert.Equal(1, await first.AuditEvents.CountAsync(item => item.Action == "finance.meter_connection_changed"));
    }

    [PostgreSqlFact]
    public async Task PostgreSqlBatchAndPreviewCancellationRollBackAndReleaseTheirSession()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var (garages, setting) = await SeedAsync(context);
        var existingAuditIds = await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        foreach (var preview in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var service = CreateService(context,
                new CancelingBaseline(FinanceServiceTestFactory.Create(context), cancellation));
            if (preview)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PreviewAsync(
                    new(garages[0].Id, garages[0].Version, [new(setting.MeterKind!, 250, ConnectedOn)]),
                    null, cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConnectBatchAsync(
                    new(setting.MeterKind!, ConnectedOn, [new(garages[0].Id, 250)]), null, cancellation.Token));
            }
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.False(await context.GarageMeterConnections.AnyAsync());
            Assert.False(await context.MeterDevices.AnyAsync());
            Assert.Equal(existingAuditIds, await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync());
        }
        Assert.True((await CreateService(context).ConnectBatchAsync(
            new(setting.MeterKind!, ConnectedOn, [new(garages[0].Id, 250)]), null, CancellationToken.None)).Succeeded);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlAbortedTransactionIsRolledBackBeforeAdvisoryUnlock()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var (garages, setting) = await SeedAsync(context);
        var existingAuditIds = await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        var service = CreateService(context, unitOfWork: new FailingPostgreSqlConstraint(context));
        var result = await service.ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn,
            [new(garages[0].Id, 250), new(garages[1].Id, 0)]), null, CancellationToken.None);
        Assert.Equal("meter_connection_changed", result.ErrorCode);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.False(await context.GarageMeterConnections.AnyAsync());
        Assert.False(await context.MeterDevices.AnyAsync());
        Assert.Equal(existingAuditIds, await context.AuditEvents.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync());
        var retry = await CreateService(context).ConnectBatchAsync(new(setting.MeterKind!, ConnectedOn,
            [new(garages[1].Id, 0), new(garages[0].Id, 250)]), null, CancellationToken.None);
        Assert.True(retry.Succeeded, retry.ErrorMessage);
    }

    private static GarageMeterConnectionService CreateService(GarageBalanceDbContext context,
        IGarageMeterBaselineService? baseline = null, IApplicationUnitOfWork? unitOfWork = null) => new(
        new EfGarageRepository(context, new TestBusinessDateProvider(September)), new EfMeterReadingRepository(context),
        baseline ?? FinanceServiceTestFactory.Create(context), unitOfWork ?? new EfApplicationUnitOfWork(context));

    private static async Task<(Garage[] Garages, ChargeServiceSetting Setting)> SeedAsync(GarageBalanceDbContext context)
    {
        var garages = new[]
        {
            new Garage { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Number = "BATCH-1", RegisteredOn = new DateOnly(2026, 1, 1), InitialWaterMeterValue = 50 },
            new Garage { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Number = "BATCH-2", RegisteredOn = new DateOnly(2026, 1, 1), InitialWaterMeterValue = 50 }
        };
        var income = new IncomeType { Code = "connection_batch", Name = "Подключаемая услуга" };
        var tariff = new Tariff { Name = "Тариф подключения", Rate = 5, CalculationBase = TariffCalculationBases.MeterElectricity, EffectiveFrom = new DateOnly(2026, 1, 1) };
        var setting = new ChargeServiceSetting
        {
            Name = "Новая услуга",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            IncomeType = income,
            Tariff = tariff,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            AppliesToSelectedGarages = true,
            GarageIds = garages.Select(garage => garage.Id).ToArray()
        };
        context.AddRange(garages);
        context.AddRange(income, tariff, setting);
        await context.SaveChangesAsync();
        return (garages, setting);
    }

    private sealed class FailingAfterSave(GarageBalanceDbContext context) : IApplicationUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            throw new ApplicationConcurrencyException(new InvalidOperationException("Synthetic conflict after persistence."));
        }
    }

    private sealed class FailingPostgreSqlConstraint(GarageBalanceDbContext context) : IApplicationUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            try
            {
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO garage_meter_connections
                        ("Id", "GarageId", "MeterKind", "ConnectedOn", "Version", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT gen_random_uuid(), "GarageId", "MeterKind", "ConnectedOn", gen_random_uuid(), "CreatedAtUtc", "UpdatedAtUtc"
                    FROM garage_meter_connections LIMIT 1
                    """, cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ApplicationPersistenceConflictException(exception);
            }
        }
    }

    private sealed class CancelingBaseline(IGarageMeterBaselineService inner, CancellationTokenSource? cancellation) : IGarageMeterBaselineService
    {
        public Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartValuesAsync(Garage garage, CancellationToken cancellationToken) => inner.GetStartValuesAsync(garage, cancellationToken);
        public Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartServicesForNewGarageAsync(CancellationToken cancellationToken) => inner.GetStartServicesForNewGarageAsync(cancellationToken);
        public async Task<FinanceResult<GarageMeterStartValueApplyResult>> ApplyStartValuesAsync(Garage garage,
            IReadOnlyList<UpsertGarageMeterStartValueRequest> requested, Guid? actorUserId, CancellationToken cancellationToken)
        {
            var result = await inner.ApplyStartValuesAsync(garage, requested, actorUserId, cancellationToken);
            cancellation?.Cancel();
            return result;
        }
    }
}
