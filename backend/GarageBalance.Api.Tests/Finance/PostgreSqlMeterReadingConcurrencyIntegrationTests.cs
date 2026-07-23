using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlMeterReadingConcurrencyIntegrationTests
{
    [PostgreSqlFact]
    public async Task MeterReadingUpdate_RecalculatesUnpaidAccrualAndRejectsPaidAccrual()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        Guid incomeTypeId;
        Guid tariffId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-METER-ACCRUAL",
                PeopleCount = 1,
                FloorCount = 1,
                InitialWaterMeterValue = 10m
            };
            var incomeType = await seedContext.IncomeTypes.SingleAsync(item => item.Code == "water");
            var tariff = new Tariff
            {
                Name = "Вода по счетчику",
                CalculationBase = TariffCalculationBases.MeterWater,
                Rate = 50m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            };
            seedContext.AddRange(garage, tariff);
            await seedContext.SaveChangesAsync();
            garageId = garage.Id;
            incomeTypeId = incomeType.Id;
            tariffId = tariff.Id;
        }

        var now = new FixedTimeProvider(new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context, now);
        var reading = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        Assert.True((await service.GenerateRegularAccrualsAsync(
            new GenerateRegularAccrualsRequest(incomeTypeId, tariffId, new DateOnly(2026, 6, 1), null),
            null,
            CancellationToken.None)).Succeeded);

        var recalculated = await service.UpdateMeterReadingAsync(
            reading.Value!.Id,
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, null, reading.Value.Version),
            null,
            CancellationToken.None);
        Assert.True(recalculated.Succeeded, recalculated.ErrorMessage);
        Assert.Equal(400m, (await context.Accruals.SingleAsync()).Amount);
        Assert.True((await service.CreateIncomeAsync(
            new CreateIncomeOperationRequest(garageId, incomeTypeId, new DateOnly(2026, 6, 22), new DateOnly(2026, 6, 1), 100m, "PG-PARTIAL-METER", null),
            null,
            CancellationToken.None)).Succeeded);

        var conflict = await service.UpdateMeterReadingAsync(
            recalculated.Value!.Id,
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 23), 20m, null, recalculated.Value.Version),
            null,
            CancellationToken.None);

        Assert.False(conflict.Succeeded);
        Assert.Equal("meter_reading_accrual_paid", conflict.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(18m, (await context.MeterReadings.SingleAsync()).CurrentValue);
        Assert.Equal(400m, (await context.Accruals.SingleAsync()).Amount);
        Assert.Equal(100m, (await context.AccrualPaymentAllocations.SingleAsync(item => item.IsActive)).Amount);
    }

    [PostgreSqlFact]
    public async Task HistoricalCorrection_EnforcesMonthBoundaryAndPersistsReasonedAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-METER-HISTORY",
                PeopleCount = 1,
                FloorCount = 1,
                InitialWaterMeterValue = 10m
            };
            seedContext.Add(garage);
            await seedContext.SaveChangesAsync();
            garageId = garage.Id;
        }

        var now = new FixedTimeProvider(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context, now);
        var actorUserId = Guid.NewGuid();
        var past = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 20), 15m, null),
            null,
            CancellationToken.None);
        var future = await FinanceServiceTestFactory.Create(
            context,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero))).CreateMeterReadingAsync(
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20), 25m, null),
            null,
            CancellationToken.None);

        var ordinaryPastUpdate = await service.UpdateMeterReadingAsync(
            past.Value!.Id,
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), 18m, null, past.Value.Version),
            actorUserId,
            CancellationToken.None);
        var ordinaryFutureUpdate = await service.UpdateMeterReadingAsync(
            future.Value!.Id,
            new CreateMeterReadingRequest(garageId, MeterKinds.Water, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 21), 26m, null, future.Value.Version),
            actorUserId,
            CancellationToken.None);
        var corrected = await service.CorrectHistoricalMeterReadingAsync(
            past.Value.Id,
            new CorrectHistoricalMeterReadingRequest(
                new DateOnly(2026, 6, 21),
                18m,
                null,
                "Сверка PostgreSQL",
                past.Value.Version),
            actorUserId,
            CancellationToken.None);

        Assert.Equal("meter_reading_current_month_required", ordinaryPastUpdate.ErrorCode);
        Assert.Equal("meter_reading_current_month_required", ordinaryFutureUpdate.ErrorCode);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        Assert.Equal(18m, corrected.Value!.CurrentValue);
        context.ChangeTracker.Clear();
        var persistedReadings = await context.MeterReadings
            .OrderBy(item => item.AccountingMonth)
            .ToListAsync();
        Assert.Collection(
            persistedReadings,
            item => Assert.Equal(18m, item.CurrentValue),
            item => Assert.Equal(25m, item.CurrentValue));
        Assert.DoesNotContain(await context.AuditEvents.ToListAsync(), item => item.Action == "finance.meter_reading_updated");
        var audit = await context.AuditEvents.SingleAsync(item => item.Action == "finance.meter_reading_historical_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Сверка PostgreSQL", metadata.RootElement.GetProperty("reason").GetString());
    }

    [PostgreSqlFact]
    public async Task PaymentFormCommand_CreatesSingleReadingWhenRequestsRace()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Guid garageId;
            await using (var seedContext = database.CreateContext())
            {
                var garage = new Garage
                {
                    Number = $"PG-METER-CREATE-RACE-{attempt}",
                    PeopleCount = 1,
                    FloorCount = 1,
                    InitialWaterMeterValue = 10m
                };
                seedContext.Add(garage);
                await seedContext.SaveChangesAsync();
                garageId = garage.Id;
            }

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<FinanceResult<MeterReadingDto>> CreateAsync(decimal value)
            {
                await using var context = database.CreateContext();
                var service = FinanceServiceTestFactory.Create(context);
                await start.Task;
                return await service.SavePaymentFormMeterReadingAsync(
                    new SavePaymentFormMeterReadingRequest(
                        garageId,
                        MeterKinds.Water,
                        new DateOnly(2026, 6, 1),
                        new DateOnly(2026, 6, 20),
                        value,
                        null),
                    null,
                    CancellationToken.None);
            }

            var requests = new[] { CreateAsync(15m + attempt), CreateAsync(25m + attempt) };
            start.SetResult();
            var results = await Task.WhenAll(requests);

            Assert.Single(results, result => result.Succeeded);
            var conflict = Assert.Single(results, result => !result.Succeeded);
            Assert.Equal("meter_reading_conflict", conflict.ErrorCode);
            await using var assertionContext = database.CreateContext();
            Assert.Equal(1, await assertionContext.MeterReadings.CountAsync(item => item.GarageId == garageId));
        }
    }

    [PostgreSqlFact]
    public async Task MeterReading_RejectsSecondDatabaseUpdateLoadedFromSameVersion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid meterReadingId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-METER-CONCURRENCY",
                PeopleCount = 1,
                FloorCount = 1,
                InitialWaterMeterValue = 10m
            };
            var reading = new MeterReading
            {
                Garage = garage,
                MeterKind = MeterKinds.Water,
                AccountingMonth = new DateOnly(2026, 6, 1),
                ReadingDate = new DateOnly(2026, 6, 20),
                CurrentValue = 15m,
                PreviousValue = 10m,
                Consumption = 5m
            };
            seedContext.Add(reading);
            await seedContext.SaveChangesAsync();
            meterReadingId = reading.Id;
        }

        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstReading = await firstContext.MeterReadings.SingleAsync(item => item.Id == meterReadingId);
        var staleReading = await staleContext.MeterReadings.SingleAsync(item => item.Id == meterReadingId);
        Assert.Equal(firstReading.Version, staleReading.Version);

        firstReading.CurrentValue = 17m;
        firstReading.Consumption = 7m;
        firstReading.Version = Guid.NewGuid();
        await new EfApplicationUnitOfWork(firstContext).SaveChangesAsync(CancellationToken.None);

        staleReading.CurrentValue = 19m;
        staleReading.Consumption = 9m;
        staleReading.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<ApplicationConcurrencyException>(() =>
            new EfApplicationUnitOfWork(staleContext).SaveChangesAsync(CancellationToken.None));

        await using var assertionContext = database.CreateContext();
        var persisted = await assertionContext.MeterReadings.SingleAsync(item => item.Id == meterReadingId);
        Assert.Equal(17m, persisted.CurrentValue);
        Assert.Equal(7m, persisted.Consumption);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
