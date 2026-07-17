using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlManualWaterMeterReadingIntegrationTests
{
    [PostgreSqlFact]
    public async Task WaterReading_RequiresRealBaselineAndUsesPreviousManualReading()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seedContext = database.CreateContext())
        {
            var seededGarage = new Garage
            {
                Number = "PG-MANUAL-WATER",
                PeopleCount = 1,
                FloorCount = 1,
                InitialWaterMeterValue = null
            };
            seedContext.Garages.Add(seededGarage);
            await seedContext.SaveChangesAsync();
            garageId = seededGarage.Id;
        }

        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);
        var missingBaseline = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 20),
                15m,
                null),
            null,
            CancellationToken.None);

        Assert.False(missingBaseline.Succeeded);
        Assert.Equal("water_meter_reading_baseline_required", missingBaseline.ErrorCode);
        Assert.Empty(await context.MeterReadings.ToListAsync());

        var garage = await context.Garages.SingleAsync(item => item.Id == garageId);
        garage.InitialWaterMeterValue = 10m;
        await context.SaveChangesAsync();
        var first = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 5, 20),
                15m,
                null),
            null,
            CancellationToken.None);
        garage.InitialWaterMeterValue = null;
        await context.SaveChangesAsync();
        var next = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 6, 20),
                18m,
                null),
            null,
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.Equal(15m, next.Value!.PreviousValue);
        Assert.Equal(3m, next.Value.Consumption);
        Assert.Equal(2, await context.MeterReadings.CountAsync());
    }
}
