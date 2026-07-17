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
        var future = await service.CreateMeterReadingAsync(
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
        var audit = await context.AuditEvents.SingleAsync(item => item.Action == "finance.meter_reading_historical_updated");
        Assert.Equal(actorUserId, audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("Сверка PostgreSQL", metadata.RootElement.GetProperty("reason").GetString());
    }

    [PostgreSqlFact]
    public async Task PaymentFormCommand_CreatesSingleReadingWhenRequestsRace()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var garage = new Garage
            {
                Number = "PG-METER-CREATE-RACE",
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

        var requests = new[] { CreateAsync(15m), CreateAsync(16m) };
        start.SetResult();
        var results = await Task.WhenAll(requests);

        Assert.Single(results, result => result.Succeeded);
        var conflict = Assert.Single(results, result => !result.Succeeded);
        Assert.Equal("meter_reading_conflict", conflict.ErrorCode);
        await using var assertionContext = database.CreateContext();
        Assert.Equal(1, await assertionContext.MeterReadings.CountAsync());
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
