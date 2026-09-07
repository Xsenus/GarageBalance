using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlGarageInitialMeterReadingMigrationTests
{
    [PostgreSqlFact]
    public async Task FirstReadingCreatesAccrualInBusinessRegistrationMonthBeforeComputerMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = MonthPeriod.CurrentLocalMonth().AddMonths(-1);
        var setting = await context.ChargeServiceSettings.Include(item => item.Tariff).Include(item => item.IncomeType)
            .SingleAsync(item => item.IncomeType != null && item.IncomeType.Code == MeterKinds.Water && !item.IsArchived);
        setting.IsRegular = true;
        setting.IsMetered = true;
        setting.PeriodicityMonths = 1;
        setting.AccrualStartMonth = 1;
        setting.Tariff!.CalculationBase = TariffCalculationBases.MeterWater;
        setting.Tariff.Rate = 50m;
        setting.Tariff.EffectiveFrom = month.AddMonths(-1);
        await context.SaveChangesAsync();
        var garage = await DictionaryServiceTestFactory.Create(context, month.AddDays(20))
            .CreateGarageAsync(new UpsertGarageRequest("BUSINESS-BASELINE", 1, 1, null, 0, 10, null, null), null, CancellationToken.None);
        Assert.True(garage.Succeeded, garage.ErrorMessage);
        var finance = FinanceServiceTestFactory.Create(context);
        var result = await finance.CreateMeterReadingAsync(new CreateMeterReadingRequest(garage.Value!.Id, MeterKinds.Water, month, month.AddDays(25), 15, null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(5m, result.Value!.Consumption);
        var accrual = await context.Accruals.SingleAsync(item => item.GarageId == garage.Value.Id);
        Assert.Equal(month, accrual.AccountingMonth);
        Assert.Equal(250m, accrual.Amount);
        var worksheet = await finance.GetGarageIncomeWorksheetAsync(garage.Value.Id, new GarageIncomeWorksheetRequest(month, month), CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(250m, Assert.Single(worksheet.Value!.Rows, item => item.IncomeTypeId == setting.IncomeTypeId).AccrualAmount);
    }

    [PostgreSqlFact]
    public async Task MigrationPreservesLegacyValuesAndNewGaragePersistsItsBusinessMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var legacy = new Garage { Number = "BASELINE-LEGACY", PeopleCount = 1, FloorCount = 1, InitialWaterMeterValue = 12.5m, InitialElectricityMeterValue = 100m };
        context.Garages.Add(legacy);
        await context.SaveChangesAsync();
        await context.GetService<IMigrator>().MigrateAsync("20260905082209_AddIndependentSupplierServices");
        await context.Database.MigrateAsync();
        context.ChangeTracker.Clear();
        var reloaded = await context.Garages.SingleAsync(item => item.Id == legacy.Id);
        Assert.Null(reloaded.InitialMeterReadingMonth);
        Assert.Null(reloaded.RegisteredOn);
        Assert.Equal(12.5m, reloaded.InitialWaterMeterValue);
        Assert.Equal(100m, reloaded.InitialElectricityMeterValue);
        var result = await DictionaryServiceTestFactory.Create(context, new DateOnly(2027, 1, 15))
            .CreateGarageAsync(new UpsertGarageRequest("BASELINE-NEW", 1, 1, null, 0, 0, 250.125m, null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var created = await context.Garages.SingleAsync(item => item.Id == result.Value!.Id);
        Assert.Equal(new DateOnly(2026, 12, 1), created.InitialMeterReadingMonth);
        Assert.Equal(new DateOnly(2027, 1, 15), created.RegisteredOn);
        var repository = new EfMeterReadingRepository(context);
        foreach (var (kind, expected) in new[] { (MeterKinds.Water, (decimal?)0m), (MeterKinds.Electricity, (decimal?)250.125m), ("custom_meter", (decimal?)null) })
        {
            var page = await repository.GetYearPageAsync(2026, kind, 0, 100, CancellationToken.None);
            Assert.Equal(expected, Assert.Single(page.Garages, item => item.Id == created.Id).InitialReadingValue);
            Assert.Empty(page.Readings);
        }
        Assert.Empty(await context.MeterReadings.ToListAsync());
        Assert.Empty(await context.Accruals.Where(item => item.GarageId == created.Id).ToListAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(new DateOnly(2026, 12, 1), (await context.Garages.AsNoTracking().SingleAsync(item => item.Id == created.Id)).InitialMeterReadingMonth);
    }
}
