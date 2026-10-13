using System.Data.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionWorksheetConcurrencyTests
{
    private static readonly DateOnly Month = new(2026, 9, 1);
    private static readonly DateOnly CorrectedDate = Month.AddDays(14);

    [Fact]
    public async Task SqliteWorksheetReadsCorrectedMeterInputsAfterTheAllocationBoundary()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var fixture = await SeedAsync(database.Context);
        var capture = new MeterInputCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(database.Context.Database.GetDbConnection()).AddInterceptors(capture).Options;
        await using var worksheetContext = new GarageBalanceDbContext(options);
        var readsBeforeBoundary = -1;
        var boundary = new AllocationBoundary(new EfAccrualPaymentAllocationRepository(worksheetContext), fixture.Key,
            async cancellationToken =>
            {
                readsBeforeBoundary = capture.ReadCount;
                await CorrectAsync(database.Context, fixture, cancellationToken);
            });
        var result = await FinanceServiceTestFactory.Create(worksheetContext, businessDate: Month.AddDays(29), allocationRepository: boundary)
            .CalculateGarageIncomeWorksheetAsync(fixture.GarageId, new(Month, Month), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(0, readsBeforeBoundary);
        Assert.Equal(1, capture.ReadCount);
        Assert.Equal(1, boundary.CallCount);
        await AssertCorrectedAsync(database.Context, fixture);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlWorksheetWaitsForBaselineCorrectionBeforeReadingMeterInputs()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Fixture fixture;
        await using (var seed = database.CreateContext()) fixture = await SeedAsync(seed);
        await using var correctionContext = database.CreateContext();
        var allocationLease = await new EfAccrualPaymentAllocationRepository(correctionContext)
            .AcquireRebuildLockAsync([fixture.Key], CancellationToken.None);
        var capture = new MeterInputCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(capture).Options;
        await using var worksheetContext = new GarageBalanceDbContext(options);
        using var cancellation = new CancellationTokenSource();
        var worksheetTask = FinanceServiceTestFactory.Create(worksheetContext, businessDate: Month.AddDays(29))
            .CalculateGarageIncomeWorksheetAsync(fixture.GarageId, new(Month, Month), null, cancellation.Token);
        try
        {
            await WaitForAllocationWaiterAsync(correctionContext);
            Assert.Equal(0, capture.ReadCount);
            // The correction re-enters its own session's allocation lock. The worksheet
            // remains blocked until the corrected baseline, reading and accrual commit.
            await CorrectAsync(correctionContext, fixture, CancellationToken.None);
            await allocationLease.DisposeAsync();
            var result = await worksheetTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(1, capture.ReadCount);
            await AssertCorrectedAsync(correctionContext, fixture);
        }
        finally
        {
            await allocationLease.DisposeAsync();
            await cancellation.CancelAsync();
            try { await worksheetTask; }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<Fixture> SeedAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage { Number = "WORKSHEET-CONNECTION-LOCK", RegisteredOn = new DateOnly(2026, 1, 1) };
        var income = new IncomeType { Code = "meter_connection_lock", Name = "Услуга блокировки" };
        var tariff = new Tariff
        {
            Name = "Тариф блокировки",
            Rate = 5m,
            CalculationBase = TariffCalculationBases.MeterElectricity,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Услуга блокировки",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            IncomeType = income,
            Tariff = tariff,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            AppliesToSelectedGarages = true,
            GarageIds = [garage.Id]
        };
        context.AddRange(garage, income, tariff, setting);
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: Month.AddDays(29));
        var connected = await finance.ApplyStartValuesAsync(garage, [new(setting.MeterKind!, 250m, Month.AddDays(9))], null, CancellationToken.None);
        Assert.True(connected.Succeeded, connected.ErrorMessage);
        await using (connected.Value!)
        {
            await context.SaveChangesAsync();
        }
        var reading = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, Month, Month.AddDays(29), 273m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(115m, (await context.Accruals.SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == income.Id)).Amount);
        context.ChangeTracker.Clear();
        return new(garage.Id, income.Id, setting.MeterKind!);
    }

    private static async Task CorrectAsync(GarageBalanceDbContext context, Fixture fixture, CancellationToken cancellationToken)
    {
        var garage = await context.Garages.SingleAsync(item => item.Id == fixture.GarageId, cancellationToken);
        var connection = await context.GarageMeterConnections.SingleAsync(item => item.GarageId == fixture.GarageId && item.MeterKind == fixture.Kind, cancellationToken);
        var result = await FinanceServiceTestFactory.Create(context, businessDate: Month.AddDays(29)).ApplyStartValuesAsync(garage,
            [new(fixture.Kind, 260m, CorrectedDate, connection.Version, "Уточнение подключения")], null, cancellationToken);
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using (result.Value!)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        Assert.Equal(65m, (await context.Accruals.SingleAsync(item => item.GarageId == fixture.GarageId && item.IncomeTypeId == fixture.IncomeId, cancellationToken)).Amount);
    }

    private static async Task AssertCorrectedAsync(GarageBalanceDbContext context, Fixture fixture)
    {
        context.ChangeTracker.Clear();
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == fixture.GarageId && item.IncomeTypeId == fixture.IncomeId);
        Assert.Equal(65m, accrual.Amount);
        var details = RegularAccrualCalculator.Deserialize(accrual.CalculationDetailsJson)!;
        Assert.Equal(260m, details.PreviousMeterValue);
        Assert.Equal(13m, details.MeterConsumption);
        Assert.Equal(CorrectedDate, details.MeterConnectedOn);
        var reading = await context.MeterReadings.SingleAsync(item => item.GarageId == fixture.GarageId);
        Assert.Equal((260m, 13m), (reading.PreviousValue, reading.Consumption));
    }

    private static async Task WaitForAllocationWaiterAsync(GarageBalanceDbContext context)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT EXISTS (SELECT 1 FROM pg_locks
                    WHERE locktype = 'advisory' AND NOT granted
                      AND database = (SELECT oid FROM pg_database WHERE datname = current_database()))
                """;
            if (Convert.ToBoolean(await command.ExecuteScalarAsync())) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("The worksheet did not wait for the shared allocation lock.");
    }

    private sealed record Fixture(Guid GarageId, Guid IncomeId, string Kind)
    {
        public AccrualPaymentAllocationKey Key => new(GarageId, IncomeId);
    }

    private sealed class MeterInputCapture : DbCommandInterceptor
    {
        private int readCount;
        public int ReadCount => Volatile.Read(ref readCount);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UNION ALL", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("garage_meter_connections", StringComparison.Ordinal)) Interlocked.Increment(ref readCount);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AllocationBoundary(IAccrualPaymentAllocationRepository inner, AccrualPaymentAllocationKey key,
        Func<CancellationToken, Task> beforeAcquire) : IAccrualPaymentAllocationRepository
    {
        public int CallCount { get; private set; }
        public async Task<IAsyncDisposable> AcquireRebuildLockAsync(IReadOnlyCollection<AccrualPaymentAllocationKey> keys, CancellationToken cancellationToken)
        {
            if (CallCount == 0 && keys.Contains(key))
            {
                CallCount++;
                await beforeAcquire(cancellationToken);
            }
            return await inner.AcquireRebuildLockAsync(keys, cancellationToken);
        }
        public Task<IAsyncDisposable> AcquireGarageIncomeWorksheetLockAsync(Guid garageId, CancellationToken cancellationToken) =>
            inner.AcquireGarageIncomeWorksheetLockAsync(garageId, cancellationToken);
        public Task<AccrualPaymentAllocationRebuildResult> RebuildAsync(IReadOnlyCollection<AccrualPaymentAllocationKey> keys, CancellationToken cancellationToken) =>
            inner.RebuildAsync(keys, cancellationToken);
        public Task<bool> HasActiveAllocationAsync(IReadOnlyCollection<Guid> accrualIds, CancellationToken cancellationToken) =>
            inner.HasActiveAllocationAsync(accrualIds, cancellationToken);
        public Task<IReadOnlySet<Guid>> GetActivelyAllocatedAccrualIdsAsync(IReadOnlyCollection<Guid> accrualIds, CancellationToken cancellationToken) =>
            inner.GetActivelyAllocatedAccrualIdsAsync(accrualIds, cancellationToken);
        public Task<decimal> GetActiveAllocatedAmountAsync(Guid accrualId, CancellationToken cancellationToken) =>
            inner.GetActiveAllocatedAmountAsync(accrualId, cancellationToken);
    }
}
