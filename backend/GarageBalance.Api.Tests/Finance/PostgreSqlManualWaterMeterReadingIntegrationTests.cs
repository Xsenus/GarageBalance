using GarageBalance.Api.Application.Common;
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
        Guid waterIncomeTypeId;
        Guid membershipIncomeTypeId;
        var currentMonth = MonthPeriod.CurrentLocalMonth();
        await using (var seedContext = database.CreateContext())
        {
            var seededGarage = new Garage
            {
                Number = "PG-MANUAL-WATER",
                PeopleCount = 1,
                FloorCount = 1,
                InitialWaterMeterValue = null
            };
            waterIncomeTypeId = await seedContext.IncomeTypes
                .Where(item => item.Code == MeterKinds.Water && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            membershipIncomeTypeId = await seedContext.IncomeTypes
                .Where(item => item.Code == "membership" && !item.IsArchived)
                .Select(item => item.Id)
                .SingleAsync();
            seedContext.Garages.Add(seededGarage);
            await seedContext.SaveChangesAsync();
            seedContext.Accruals.Add(new Accrual
            {
                GarageId = seededGarage.Id,
                IncomeTypeId = membershipIncomeTypeId,
                AccountingMonth = currentMonth,
                DueDate = currentMonth.AddMonths(1).AddDays(-1),
                OverdueFromDate = currentMonth.AddMonths(1),
                Amount = 700m,
                Source = "regular"
            });
            await seedContext.SaveChangesAsync();
            garageId = seededGarage.Id;
        }

        await using var context = database.CreateContext();
        var service = FinanceServiceTestFactory.Create(context);
        var firstMonth = currentMonth.AddMonths(-4);
        var secondMonth = currentMonth.AddMonths(-3);
        var sameValueMonth = currentMonth.AddMonths(-2);
        var fractionalMonth = currentMonth.AddMonths(-1);
        var missingValue = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                firstMonth,
                firstMonth.AddDays(19),
                null,
                null),
            null,
            CancellationToken.None);
        var missingBaseline = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                firstMonth,
                firstMonth.AddDays(19),
                15m,
                null),
            null,
            CancellationToken.None);

        Assert.False(missingValue.Succeeded);
        Assert.Equal("meter_reading_value_required", missingValue.ErrorCode);
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
                firstMonth,
                firstMonth.AddDays(19),
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
                secondMonth,
                secondMonth.AddDays(19),
                18m,
                null),
            null,
            CancellationToken.None);
        var same = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                sameValueMonth,
                sameValueMonth.AddDays(19),
                18m,
                null),
            null,
            CancellationToken.None);
        var fractional = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                fractionalMonth,
                fractionalMonth.AddDays(19),
                18.375m,
                null),
            null,
            CancellationToken.None);
        var decreased = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                currentMonth,
                currentMonth.AddDays(19),
                18.25m,
                null),
            null,
            CancellationToken.None);
        var missingWorksheet = await service.GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(currentMonth, currentMonth),
            CancellationToken.None);
        var current = await service.CreateMeterReadingAsync(
            new CreateMeterReadingRequest(
                garageId,
                MeterKinds.Water,
                currentMonth,
                currentMonth.AddDays(19),
                18.5m,
                null),
            null,
            CancellationToken.None);
        var completedWorksheet = await service.GetGarageIncomeWorksheetAsync(
            garageId,
            new GarageIncomeWorksheetRequest(currentMonth, currentMonth),
            CancellationToken.None);

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.True(next.Succeeded, next.ErrorMessage);
        Assert.Equal(15m, next.Value!.PreviousValue);
        Assert.Equal(3m, next.Value.Consumption);
        Assert.True(same.Succeeded, same.ErrorMessage);
        Assert.Equal(18m, same.Value!.PreviousValue);
        Assert.Equal(0m, same.Value.Consumption);
        Assert.True(fractional.Succeeded, fractional.ErrorMessage);
        Assert.Equal(18m, fractional.Value!.PreviousValue);
        Assert.Equal(18.375m, fractional.Value.CurrentValue);
        Assert.Equal(0.375m, fractional.Value.Consumption);
        Assert.False(decreased.Succeeded);
        Assert.Equal("meter_reading_decreased", decreased.ErrorCode);
        Assert.True(missingWorksheet.Succeeded, missingWorksheet.ErrorMessage);
        var missingWater = Assert.Single(missingWorksheet.Value!.Rows, row => row.IncomeTypeId == waterIncomeTypeId);
        Assert.Null(missingWater.MeterValue);
        Assert.Equal(0m, missingWater.AccrualAmount);
        var membershipBeforeReading = Assert.Single(missingWorksheet.Value.Rows, row => row.IncomeTypeId == membershipIncomeTypeId);
        Assert.Equal(700m, membershipBeforeReading.AccrualAmount);
        Assert.True(current.Succeeded, current.ErrorMessage);
        Assert.Equal(18.375m, current.Value!.PreviousValue);
        Assert.Equal(0.125m, current.Value.Consumption);
        Assert.True(completedWorksheet.Succeeded, completedWorksheet.ErrorMessage);
        var completedWater = Assert.Single(completedWorksheet.Value!.Rows, row => row.IncomeTypeId == waterIncomeTypeId);
        Assert.Equal(18.5m, completedWater.MeterValue);
        Assert.Equal(0.125m, completedWater.MeterConsumption);
        var membershipAfterReading = Assert.Single(completedWorksheet.Value.Rows, row => row.IncomeTypeId == membershipIncomeTypeId);
        Assert.Equal(700m, membershipAfterReading.AccrualAmount);
        Assert.Equal(5, await context.MeterReadings.CountAsync());
    }
}
