using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class AnnualBatchEffectiveMonthTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly November = new(2026, 11, 1);

    [Fact]
    public void EffectiveMonthUsesLaterNormalizedMonth()
    {
        Assert.Equal(September, AnnualAccrualPolicy.ResolveAccountingMonth(January.AddDays(10), September.AddDays(20)));
        Assert.Equal(September, AnnualAccrualPolicy.ResolveAccountingMonth(September.AddDays(10), January));
    }

    [Theory]
    [InlineData("fixed", false)]
    [InlineData("fixed", true)]
    [InlineData("people", false)]
    [InlineData("meter", false)]
    [InlineData("individual", false)]
    [InlineData("gap", false)]
    public async Task BatchCardAndWorksheetUseActualBillingMonth(string scenario, bool registrationFallback)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await VerifyParityAsync(db.Context, scenario, registrationFallback);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_BatchCardAndWorksheetUseActualBillingMonth()
    {
        await using var db = await PostgreSqlTestDatabase.CreateAsync();
        foreach (var scenario in new[] { "fixed", "people", "meter", "individual", "gap" })
        {
            await using var scenarioDb = await PostgreSqlTestDatabase.CreateAsync();
            await using var context = scenarioDb.CreateContext();
            await VerifyParityAsync(context, scenario, false);
        }
    }

    private static async Task VerifyParityAsync(GarageBalanceDbContext context, string scenario, bool registrationFallback)
    {
        var (batchGarage, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        batchGarage.Number = $"synthetic-batch-{scenario}-{registrationFallback}";
        var cardGarage = new Garage { Number = $"synthetic-card-{scenario}", AccountingStartMonth = September, RegisteredOn = September, PeopleCount = 2, FloorCount = 1 };
        var worksheetGarage = new Garage { Number = $"synthetic-sheet-{scenario}", AccountingStartMonth = September, RegisteredOn = September, PeopleCount = 2, FloorCount = 1 };
        batchGarage.PeopleCount = 2;
        if (registrationFallback)
        {
            batchGarage.AccountingStartMonth = null;
            batchGarage.RegisteredOn = September;
            cardGarage.AccountingStartMonth = null;
            worksheetGarage.AccountingStartMonth = null;
        }
        var garages = new[] { batchGarage, cardGarage, worksheetGarage };
        setting.AppliesToSelectedGarages = scenario != "individual";
        setting.GarageIds = garages.Select(garage => garage.Id).ToArray();
        var calculationBase = scenario == "people" ? TariffCalculationBases.People
            : scenario == "meter" ? TariffCalculationBases.MeterWater : TariffCalculationBases.Fixed;
        setting.Tariff!.CalculationBase = calculationBase;
        setting.Tariff.Rate = scenario is "people" or "meter" ? 100 : 1200;
        var dated = new Tariff { Name = "Synthetic dated annual", CalculationBase = calculationBase, Rate = scenario is "people" or "meter" ? 200 : 1800, EffectiveFrom = September };
        context.AddRange(cardGarage, worksheetGarage,
            new ChargeServiceTariffVersion { ChargeServiceSetting = setting, Tariff = setting.Tariff, EffectiveFrom = January, EffectiveTo = September.AddDays(-1) });
        if (scenario != "gap")
            context.Add(new ChargeServiceTariffVersion { ChargeServiceSetting = setting, Tariff = dated, EffectiveFrom = September });
        foreach (var garage in garages)
        {
            if (scenario == "people") context.AddRange(
                new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = January, PeopleCount = 1 },
                new GaragePeopleCountPeriod { GarageId = garage.Id, EffectiveFrom = September, PeopleCount = 2 });
            if (scenario == "meter")
            {
                setting.IsMetered = true;
                setting.MeterKind = MeterKinds.Water;
                context.AddRange(
                    new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = January, ReadingDate = January.AddDays(20), Consumption = 1, CurrentValue = 1 },
                    new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = September, ReadingDate = September.AddDays(20), Consumption = 3, CurrentValue = 4, PreviousValue = 1 });
            }
            if (scenario == "individual") context.Add(new GarageTariffAssignment
            {
                Garage = garage,
                ChargeServiceSetting = setting,
                EffectiveFrom = September,
                Tariff = new Tariff { Name = $"Synthetic dated individual {garage.Number}", CalculationBase = calculationBase, Rate = 2100, EffectiveFrom = September }
            });
        }
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        Assert.True((await service.CalculateGarageAnnualPaymentsAsync(cardGarage.Id, 2026, null, default)).Succeeded);
        Assert.True((await service.CalculateGarageIncomeWorksheetAsync(worksheetGarage.Id, new(November, November), null, default)).Succeeded);
        var batch = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, January, null), null, default);
        var ids = garages.Select(garage => garage.Id).ToArray();
        var rows = await context.Accruals.Where(row => ids.Contains(row.GarageId) && row.IncomeTypeId == setting.IncomeTypeId).ToArrayAsync();
        if (scenario == "gap") { Assert.False(batch.Succeeded); Assert.Empty(rows); return; }
        Assert.True(batch.Succeeded, batch.ErrorMessage);
        Assert.Equal(3, rows.Length);
        var expected = scenario switch { "people" => 400m, "meter" => 600m, "individual" => 2100m, _ => 1800m };
        foreach (var row in rows)
        {
            Assert.Equal(expected, row.Amount);
            Assert.Equal(September, row.AccountingMonth);
            Assert.Equal(2026, row.AccountingYear);
            Assert.Equal(dated.Id, row.TariffId);
            Assert.Equal(September, RegularAccrualCalculator.Deserialize(row.CalculationDetailsJson)!.AccountingMonth);
        }
        Assert.Single(rows.Select(row => row.DueDate).Distinct());
        var repeat = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId.Value, setting.TariffId.Value, January, null), null, default);
        Assert.False(repeat.Succeeded);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task MovedMonthManualCancellationRemainsCanceled(bool knownAnnual, bool originalMonth)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db.Context, September);
        var old = new Accrual { Garage = garage, IncomeType = setting.IncomeType!, AccountingMonth = originalMonth ? January : September, AccountingYear = knownAnnual ? 2026 : null, IsAnnualAccrual = knownAnnual ? true : null, Source = AccrualSources.Regular, Amount = 1200 };
        db.Context.Add(old);
        await db.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db.Context, businessDate: November);
        Assert.True((await service.CancelAccrualAsync(old.Id, new("Synthetic cancellation"), null, default)).Succeeded);
        var result = await service.GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, January, null), null, default);
        Assert.False(result.Succeeded);
        Assert.True((await db.Context.Accruals.SingleAsync()).IsCanceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnnualBatchDoesNotGenerateBeforeFutureStart(bool explicitStart)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db.Context, new(2026, 12, 1));
        if (!explicitStart) { garage.AccountingStartMonth = null; garage.RegisteredOn = new(2026, 12, 1); }
        await db.Context.SaveChangesAsync();
        var result = await FinanceServiceTestFactory.Create(db.Context, businessDate: November)
            .GenerateRegularAccrualsAsync(new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, January, null), null, default);
        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());
    }

    [Theory]
    [InlineData(2026, 12, false)]
    [InlineData(2026, 12, true)]
    [InlineData(2027, 1, false)]
    [InlineData(2027, 1, true)]
    public async Task ExplicitFutureAnnualBatchWorksWhileCurrentCatalogStaysWithinBusinessMonth(int year, int month, bool explicitStart)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var futureMonth = new DateOnly(year, month, 1);
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db.Context, futureMonth);
        if (!explicitStart) { garage.AccountingStartMonth = null; garage.RegisteredOn = futureMonth; }
        await db.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(db.Context, businessDate: November);
        var catalog = await service.GenerateRegularCatalogAccrualsAsync(new(November, null), null, default);
        Assert.False(catalog.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());

        var direct = await service.GenerateRegularAccrualsAsync(
            new(setting.IncomeTypeId!.Value, setting.TariffId!.Value, futureMonth, null), null, default);
        Assert.True(direct.Succeeded, direct.ErrorMessage);
        var row = Assert.Single(await db.Context.Accruals.ToArrayAsync());
        Assert.Equal(futureMonth, row.AccountingMonth);
        Assert.Equal(year, row.AccountingYear);
        Assert.Equal(1200m, row.Amount);
    }

    [Fact]
    public async Task ExplicitFutureCatalogPreservesScheduledAnnualPeriod()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await VerifyExplicitFutureCatalogAsync(db.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExplicitFutureCatalogPreservesScheduledAnnualPeriod()
    {
        await using var db = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await VerifyExplicitFutureCatalogAsync(context);
    }

    private static async Task VerifyExplicitFutureCatalogAsync(GarageBalanceDbContext context)
    {
        var (_, setting) = await AnnualAccountingStartTests.SeedAsync(context, September);
        var january = new DateOnly(2027, 1, 1);
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        var offSchedule = new DateOnly(2027, 11, 1);
        Assert.Equal(0, (await service.PreviewRegularAccrualAutomationAsync(offSchedule, default)).DueRegularServiceCount);
        Assert.False((await service.GenerateRegularCatalogAccrualsAsync(new(offSchedule, null), null, default)).Succeeded);
        Assert.Empty(await context.Accruals.ToArrayAsync());
        Assert.Equal(1, (await service.PreviewRegularAccrualAutomationAsync(january, default)).DueRegularServiceCount);
        var generated = await service.GenerateRegularCatalogAccrualsAsync(new(january, null), null, default);
        Assert.True(generated.Succeeded, generated.ErrorMessage);
        var row = Assert.Single(await context.Accruals.ToArrayAsync());
        Assert.Equal(setting.IncomeTypeId, row.IncomeTypeId);
        Assert.Equal(january, row.AccountingMonth);
        Assert.Equal(2027, row.AccountingYear);
        Assert.Equal(1200m, row.Amount);
        Assert.False((await service.GenerateRegularCatalogAccrualsAsync(new(january, null), null, default)).Succeeded);
        Assert.Single(await context.Accruals.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FutureCatalogCatchUpDoesNotAdvanceBeyondBusinessMonth(bool explicitStart)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var december = new DateOnly(2026, 12, 1);
        var (garage, _) = await AnnualAccountingStartTests.SeedAsync(db.Context, december);
        if (!explicitStart) { garage.AccountingStartMonth = null; garage.RegisteredOn = december; }
        await db.Context.SaveChangesAsync();
        var generated = await FinanceServiceTestFactory.Create(db.Context, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(december, null), null, default);
        Assert.False(generated.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentCatalogWaitsUntilGarageRegistrationMonthThenCatchesUp(bool explicitStart)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await AnnualAccountingStartTests.SeedAsync(db.Context, September);
        setting.AccrualStartMonth = 8;
        if (!explicitStart) { garage.AccountingStartMonth = null; garage.RegisteredOn = September; }
        await db.Context.SaveChangesAsync();
        var august = September.AddMonths(-1);
        var beforeStart = await FinanceServiceTestFactory.Create(db.Context, businessDate: august)
            .GenerateRegularCatalogAccrualsAsync(new(august, null), null, default);
        Assert.False(beforeStart.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());

        var due = await FinanceServiceTestFactory.Create(db.Context, businessDate: September)
            .GenerateRegularCatalogAccrualsAsync(new(September, null), null, default);
        Assert.True(due.Succeeded, due.ErrorMessage);
        var row = Assert.Single(await db.Context.Accruals.ToArrayAsync());
        Assert.Equal(September, row.AccountingMonth);
        Assert.Equal(2026, row.AccountingYear);
        Assert.Equal(1200m, row.Amount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CatalogCatchesUpOnlyCurrentYearAnnualAndCurrentMonth(bool held, bool lateTariff)
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await VerifyCatalogAsync(db.Context, held, lateTariff);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CatalogCatchesUpOnlyCurrentYearAnnualAndCurrentMonth()
    {
        await using var db = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = db.CreateContext();
        await VerifyCatalogAsync(context, false);
    }

    [Fact]
    public async Task HistoricalCatalogDoesNotBackfillAnnualOutsideBusinessYear()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await AnnualAccountingStartTests.SeedAsync(db.Context, September);
        var result = await FinanceServiceTestFactory.Create(db.Context, businessDate: new(2027, 11, 1))
            .GenerateRegularCatalogAccrualsAsync(new(November, null), null, default);
        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());
    }

    [Fact]
    public async Task CatalogDoesNotGenerateBeyondRequestedMonth()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();
        await AnnualAccountingStartTests.SeedAsync(db.Context, November);
        var result = await FinanceServiceTestFactory.Create(db.Context, businessDate: November)
            .GenerateRegularCatalogAccrualsAsync(new(September, null), null, default);
        Assert.False(result.Succeeded);
        Assert.Empty(await db.Context.Accruals.ToArrayAsync());
    }

    private static async Task VerifyCatalogAsync(GarageBalanceDbContext context, bool held, bool lateTariff = false)
    {
        var (garage, annual) = await AnnualAccountingStartTests.SeedAsync(context, September);
        if (lateTariff) annual.Tariff!.EffectiveFrom = September;
        var monthly = new ChargeServiceSetting
        {
            Name = "Synthetic monthly",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            IncomeType = new IncomeType { Name = "Synthetic monthly", Code = "synthetic_monthly_catchup" },
            Tariff = new Tariff { Name = "Synthetic monthly", Rate = 50, CalculationBase = TariffCalculationBases.Fixed, EffectiveFrom = January }
        };
        context.Add(monthly);
        if (held) context.Add(new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: November);
        var preview = await service.PreviewRegularAccrualAutomationAsync(November, default);
        Assert.Equal(held ? 1 : 2, preview.DueRegularServiceCount);
        if (held) Assert.NotEmpty(preview.Warnings);
        var result = await service.GenerateRegularCatalogAccrualsAsync(new(November, null), null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var rows = await context.Accruals.ToArrayAsync();
        Assert.Equal(held ? 1 : 2, rows.Length);
        Assert.Equal(November, Assert.Single(rows, row => row.IncomeTypeId == monthly.IncomeTypeId).AccountingMonth);
        if (!held)
        {
            var yearly = Assert.Single(rows, row => row.IncomeTypeId == annual.IncomeTypeId);
            Assert.Equal(September, yearly.AccountingMonth);
            Assert.Equal(2026, yearly.AccountingYear);
            Assert.True((await service.CancelAccrualAsync(yearly.Id, new("Synthetic cancel after catchup"), null, default)).Succeeded);
            await service.GenerateRegularCatalogAccrualsAsync(new(November, null), null, default);
            Assert.Equal(2, await context.Accruals.CountAsync());
            Assert.True(yearly.IsCanceled);
        }
    }
}
