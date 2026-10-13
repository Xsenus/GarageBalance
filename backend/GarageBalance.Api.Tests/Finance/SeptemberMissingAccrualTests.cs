using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class SeptemberMissingAccrualTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);

    [Fact]
    public async Task CorrectedStart_CreatesElectricityAndPeopleWasteWithoutRewritingPaidWater()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertSeptemberAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CorrectedStartCreatesSeptemberAccrualsAndPreservesPaidWater()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertSeptemberAsync(context);
    }

    private static async Task AssertSeptemberAsync(GarageBalanceDbContext context)
    {
        var (garage, water, electricity, waste, paidWater) = await SeedAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October.AddDays(12));
        var before = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        Assert.True(before.Succeeded, before.ErrorMessage);
        Assert.Single(before.Value!.Rows);
        Assert.Equal(water.Id, before.Value.Rows[0].IncomeTypeId);
        var corrected = await DictionaryServiceTestFactory.Create(context, October.AddDays(12)).UpdateGarageAsync(garage.Id,
            new(garage.Number, garage.PeopleCount, garage.FloorCount, null, 0, 50, 100, null, Version: garage.Version, AccountingStartMonth: September, AccountingStartReason: "Начало учёта в сентябре"), null, CancellationToken.None);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        var result = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(3, result.Value!.Rows.Count);
        var waterRow = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == water.Id);
        Assert.Equal(25m, waterRow.AccrualAmount);
        Assert.Equal(25m, waterRow.IncomeAmount);
        Assert.Equal(0m, waterRow.Debt);
        Assert.Null(waterRow.CalculationIssue);
        var electricRow = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == electricity.Id);
        Assert.Equal(100m, electricRow.AccrualAmount);
        Assert.Equal(10m, electricRow.MeterConsumption);
        Assert.Null(electricRow.CalculationIssue);
        var wasteRow = Assert.Single(result.Value.Rows, row => row.IncomeTypeId == waste.Id);
        Assert.Equal(260m, wasteRow.AccrualAmount);
        Assert.Null(wasteRow.MeterKind);
        Assert.Null(wasteRow.CalculationIssue);
        var ids = await context.Accruals.Where(row => row.AccountingMonth == September && !row.IsCanceled).Select(row => row.Id).OrderBy(id => id).ToArrayAsync();
        Assert.Equal(3, ids.Length);
        await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        Assert.Equal(ids, await context.Accruals.Where(row => row.AccountingMonth == September && !row.IsCanceled).Select(row => row.Id).OrderBy(id => id).ToArrayAsync());
        Assert.Equal(paidWater.Id, (await context.Accruals.SingleAsync(row => row.IncomeTypeId == water.Id && row.AccountingMonth == September)).Id);
        Assert.Equal(25m, (await context.AccrualPaymentAllocations.SingleAsync(row => row.AccrualId == paidWater.Id && row.IsActive)).Amount);
    }

    [Fact]
    public async Task MissingTariff_ShowsReasonThenDisappearsAfterPriceIsAvailable()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _, electricity, _, _) = await SeedAsync(context);
        garage.AccountingStartMonth = September;
        garage.InitialMeterReadingMonth = September.AddMonths(-1);
        var tariff = (await context.ChargeServiceSettings.Include(setting => setting.Tariff).SingleAsync(setting => setting.IncomeTypeId == electricity.Id)).Tariff!;
        tariff.EffectiveFrom = October;
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: October);
        var missing = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        var row = Assert.Single(missing.Value!.Rows, row => row.IncomeTypeId == electricity.Id);
        Assert.Contains("Не задан тариф", row.CalculationIssue);
        Assert.Equal(0m, row.AccrualAmount);
        Assert.DoesNotContain(await context.Accruals.ToListAsync(), accrual => accrual.IncomeTypeId == electricity.Id && accrual.AccountingMonth == September);
        tariff.EffectiveFrom = new(2026, 1, 1);
        await context.SaveChangesAsync();
        var resolved = await finance.CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        var complete = Assert.Single(resolved.Value!.Rows, row => row.IncomeTypeId == electricity.Id);
        Assert.Equal(100m, complete.AccrualAmount);
        Assert.Null(complete.CalculationIssue);
    }

    [Theory]
    [InlineData("reading", "нет показания")]
    [InlineData("baseline", "требуется стартовое показание")]
    [InlineData("connection", "услуга не подключена")]
    public async Task MissingInput_ShowsItsOwnReasonWithoutCreatingAnObligation(string missing, string expected)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _, electricity, _, _) = await SeedAsync(context);
        garage.AccountingStartMonth = September;
        garage.InitialMeterReadingMonth = September.AddMonths(-1);
        if (missing == "reading") context.MeterReadings.Remove(await context.MeterReadings.SingleAsync(reading => reading.MeterKind == MeterKinds.Electricity));
        else context.GarageMeterConnections.Add(new GarageMeterConnection
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            ConnectedOn = missing == "connection" ? October : September
        });
        await context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(context, businessDate: October)
            .CalculateGarageIncomeWorksheetAsync(garage.Id, new(September, September), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var row = Assert.Single(result.Value!.Rows, row => row.IncomeTypeId == electricity.Id);
        Assert.Contains(expected, row.CalculationIssue);
        Assert.Equal(0m, row.AccrualAmount);
        Assert.DoesNotContain(await context.Accruals.ToListAsync(), accrual => accrual.IncomeTypeId == electricity.Id && accrual.AccountingMonth == September);
    }

    private static async Task<(Garage Garage, IncomeType Water, IncomeType Electricity, IncomeType Waste, Accrual PaidWater)> SeedAsync(GarageBalanceDbContext context)
    {
        foreach (var setting in context.ChargeServiceSettings) setting.IsArchived = true;
        var garage = new Garage { Number = "synthetic-september", RegisteredOn = October.AddDays(12), PeopleCount = 2, FloorCount = 1, InitialMeterReadingMonth = September, InitialWaterMeterValue = 50, InitialElectricityMeterValue = 100 };
        var water = new IncomeType { Name = "Synthetic water", Code = "synthetic_september_water" };
        var electricity = new IncomeType { Name = "Synthetic electricity", Code = "synthetic_september_electricity" };
        var waste = new IncomeType { Name = "Synthetic waste", Code = "synthetic_september_waste" };
        foreach (var (incomeType, calculationBase, kind, rate) in new[]
        {
            (water, TariffCalculationBases.MeterWater, MeterKinds.Water, 5m),
            (electricity, TariffCalculationBases.MeterElectricity, MeterKinds.Electricity, 10m),
            (waste, TariffCalculationBases.People, MeterKinds.ForService(Guid.NewGuid()), 130m)
        }) context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = incomeType.Name,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = incomeType,
            IsMetered = true,
            MeterKind = kind,
            Tariff = new Tariff { Name = incomeType.Name, CalculationBase = calculationBase, Rate = rate, EffectiveFrom = new(2026, 1, 1) }
        });
        var paid = new Accrual { Garage = garage, IncomeType = water, AccountingMonth = September, Amount = 25, Source = AccrualSources.Regular, RequiresMeterReading = true, CalculationMeterKind = MeterKinds.Water };
        var payment = new FinancialOperation { Garage = garage, IncomeType = water, OperationKind = FinancialOperationKinds.Income, AccountingMonth = September, OperationDate = September.AddDays(20), Amount = 25 };
        context.AddRange(garage, paid, payment, new AccrualPaymentAllocation { Accrual = paid, FinancialOperation = payment, Amount = 25 },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = September, ReadingDate = September.AddDays(20), PreviousValue = 50, CurrentValue = 55, Consumption = 5 },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Electricity, AccountingMonth = September, ReadingDate = September.AddDays(20), PreviousValue = 100, CurrentValue = 110, Consumption = 10 });
        await context.SaveChangesAsync();
        return (garage, water, electricity, waste, paid);
    }
}
