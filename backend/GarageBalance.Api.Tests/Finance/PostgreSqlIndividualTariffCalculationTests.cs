using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlIndividualTariffCalculationTests
{
    [PostgreSqlFact]
    public async Task AccrualInsertionReusesExistingIncomeReferencesInsteadOfDuplicatingCatalogRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var income = new IncomeType { Name = "Ссылка вида начисления", Code = "detached_accrual_reference_test" };
        var garage = new Garage { Number = "85-DETACHED-REFERENCE" };
        context.AddRange(income, garage);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        garage = await context.Garages.SingleAsync(row => row.Id == garage.Id);
        var detached = await context.IncomeTypes.AsNoTracking().SingleAsync(row => row.Id == income.Id);
        var repository = new EfAccrualRepository(context);
        var first = new Accrual
        {
            Garage = garage,
            GarageId = garage.Id,
            IncomeType = detached,
            IncomeTypeId = income.Id,
            AccountingMonth = new(2050, 9, 1),
            Amount = 100,
            Source = AccrualSources.Manual
        };
        repository.Add(first);
        Assert.Equal(EntityState.Unchanged, context.Entry(first.IncomeType).State);
        await context.SaveChangesAsync();
        var detachedAgain = await context.IncomeTypes.AsNoTracking().SingleAsync(row => row.Id == income.Id);
        var second = new Accrual
        {
            Garage = garage,
            GarageId = garage.Id,
            IncomeType = detachedAgain,
            IncomeTypeId = income.Id,
            AccountingMonth = new(2050, 10, 1),
            Amount = 200,
            Source = AccrualSources.Manual
        };
        repository.Add(second);
        Assert.Same(first.IncomeType, second.IncomeType);
        await context.SaveChangesAsync();
        var byIdOnly = new Accrual
        {
            GarageId = garage.Id,
            IncomeTypeId = income.Id,
            AccountingMonth = new(2050, 11, 1),
            Amount = 300,
            Source = AccrualSources.Manual
        };
        repository.Add(byIdOnly);
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.IncomeTypes.CountAsync(row => row.Code == income.Code));
        Assert.Equal(3, await context.Accruals.CountAsync(row => row.IncomeTypeId == income.Id));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [PostgreSqlFact]
    public async Task AnnualPlannedAndIssuedAmountsUseTheSameIndividualTimeline()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2050, 9, 1);
        var garage = new Garage { Number = "85-ANNUAL-INDIVIDUAL", RegisteredOn = month };
        var income = new IncomeType { Name = "Индивидуальная годовая услуга", Code = "individual_annual_test" };
        var general = new Tariff { Name = "Общий годовой тариф", CalculationBase = TariffCalculationBases.Fixed, Rate = 400, EffectiveFrom = month };
        var individual = new Tariff { Name = "Индивидуальный годовой тариф", CalculationBase = TariffCalculationBases.Fixed, Rate = 200, EffectiveFrom = month };
        var setting = new ChargeServiceSetting
        {
            Name = income.Name,
            IncomeType = income,
            Tariff = general,
            IsRegular = true,
            PeriodicityMonths = 12,
            AccrualStartMonth = 9,
            UnitName = "руб."
        };
        context.Add(new GarageTariffAssignment
        {
            Garage = garage,
            GarageId = garage.Id,
            ChargeServiceSetting = setting,
            ChargeServiceSettingId = setting.Id,
            Tariff = individual,
            TariffId = individual.Id,
            EffectiveFrom = month,
            EffectiveTo = month.AddDays(14)
        });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2050, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        var planned = await service.GetGarageAnnualPaymentsAsync(garage.Id, 2050, CancellationToken.None);
        Assert.True(planned.Succeeded, planned.ErrorMessage);
        Assert.Equal(300m, Assert.Single(planned.Value!.Items, item => item.IncomeTypeId == income.Id).Amount);
        Assert.False(await context.Accruals.AnyAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id));
        var issued = await service.CalculateGarageAnnualPaymentsAsync(garage.Id, 2050, null, CancellationToken.None);
        Assert.True(issued.Succeeded, issued.ErrorMessage);
        Assert.Equal(300m, Assert.Single(issued.Value!.Items, item => item.IncomeTypeId == income.Id).Amount);
        var accrual = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id);
        Assert.Equal(2050, accrual.AccountingYear);
        Assert.Equal(300m, accrual.Amount);
    }

    [PostgreSqlFact]
    public async Task GenerationPreviewAndWorksheetUseIndividualPeriodsButPreservePaidSnapshots()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2050, 9, 1);
        var first = new Garage { Number = "85-INDIVIDUAL", RegisteredOn = month };
        var second = new Garage { Number = "86-GENERAL", RegisteredOn = month };
        var income = new IncomeType { Name = "Услуга индивидуальной ставки", Code = "individual_fixed_test" };
        var general = new Tariff { Name = "Общая ставка", CalculationBase = TariffCalculationBases.Fixed, Rate = 400, EffectiveFrom = month };
        var individual = new Tariff { Name = "Индивидуальная ставка", CalculationBase = TariffCalculationBases.Fixed, Rate = 200, EffectiveFrom = month };
        var setting = new ChargeServiceSetting
        {
            Name = income.Name,
            IncomeType = income,
            Tariff = general,
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            UnitName = "руб."
        };
        var assignment = new GarageTariffAssignment
        {
            Garage = first,
            GarageId = first.Id,
            ChargeServiceSetting = setting,
            ChargeServiceSettingId = setting.Id,
            Tariff = individual,
            TariffId = individual.Id,
            EffectiveFrom = month,
            EffectiveTo = month.AddDays(14)
        };
        context.AddRange(first, second, setting, assignment);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2050, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        var generation = await service.GenerateRegularAccrualsAsync(new(income.Id, general.Id, month, null), null, CancellationToken.None);
        Assert.True(generation.Succeeded, generation.ErrorMessage);
        var firstAccrual = await context.Accruals.SingleAsync(row => row.GarageId == first.Id && row.IncomeTypeId == income.Id);
        var paidAccrual = await context.Accruals.SingleAsync(row => row.GarageId == second.Id && row.IncomeTypeId == income.Id);
        Assert.Equal(300m, firstAccrual.Amount);
        Assert.Contains("фактически применённые тарифные периоды", firstAccrual.Comment);
        Assert.Contains("01.09.2050–15.09.2050", firstAccrual.Comment);
        Assert.Equal(400m, paidAccrual.Amount);
        var paidSnapshot = paidAccrual.CalculationDetailsJson;
        var operation = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = month,
            AccountingMonth = month,
            Amount = paidAccrual.Amount,
            GarageId = second.Id,
            IncomeTypeId = income.Id
        };
        context.AddRange(operation, new AccrualPaymentAllocation
        {
            FinancialOperation = operation,
            Accrual = paidAccrual,
            Amount = paidAccrual.Amount,
            IsActive = true
        });
        general.Rate = 600;
        individual.Rate = 100;
        await context.SaveChangesAsync();
        var preview = await service.PreviewRegularAccrualRecalculationAsync(new(income.Id, general.Id, month), CancellationToken.None);
        Assert.True(preview.Succeeded, preview.ErrorMessage);
        Assert.Contains(preview.Value!.Rows, row => row.AccrualId == firstAccrual.Id && row.ProposedAmount == 350m);
        Assert.Contains(preview.Value.Rows, row => row.AccrualId == paidAccrual.Id && row.ProposedAmount == 400m && row.IsPaid);
        var applied = await service.ApplyRegularAccrualRecalculationAsync(
            new(income.Id, general.Id, month, preview.Value.PreviewFingerprint, "Проверка индивидуальной ставки"), null, CancellationToken.None);
        Assert.True(applied.Succeeded, applied.ErrorMessage);
        Assert.Equal(350m, firstAccrual.Amount);
        Assert.Equal(400m, paidAccrual.Amount);
        Assert.Equal(paidSnapshot, paidAccrual.CalculationDetailsJson);
        var worksheet = await service.CalculateGarageIncomeWorksheetAsync(first.Id, new(month, month), null, CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(350m, Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == income.Id).AccrualAmount);
        assignment.IsArchived = true;
        await context.SaveChangesAsync();
        var restoredGeneral = await service.CalculateGarageIncomeWorksheetAsync(first.Id, new(month, month), null, CancellationToken.None);
        Assert.True(restoredGeneral.Succeeded, restoredGeneral.ErrorMessage);
        Assert.Equal(600m, Assert.Single(restoredGeneral.Value!.Rows, row => row.IncomeTypeId == income.Id).AccrualAmount);
        Assert.Equal(2, await context.Accruals.CountAsync(row => row.IncomeTypeId == income.Id && !row.IsCanceled));
        Assert.Contains(await context.AuditEvents.ToListAsync(), row => row.Action == "finance.regular_accrual_safe_recalculation_applied");
    }

    [PostgreSqlFact]
    public async Task ReadingAutomaticallyCreatesIndividualWaterAccrualAndArchiveRestoresGeneralRate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = MonthPeriod.CurrentLocalMonth();
        var water = await context.IncomeTypes.SingleAsync(row => row.Code == MeterKinds.Water && !row.IsArchived);
        var setting = await context.ChargeServiceSettings.Include(row => row.Tariff)
            .SingleAsync(row => row.IncomeTypeId == water.Id && !row.IsArchived);
        setting.IsRegular = true;
        setting.IsMetered = true;
        setting.PeriodicityMonths = 1;
        setting.AccrualStartMonth = 1;
        setting.Tariff!.Rate = 50;
        setting.Tariff.CalculationBase = TariffCalculationBases.MeterWater;
        setting.Tariff.EffectiveFrom = month.AddMonths(-1);
        var garage = new Garage { Number = "85-INDIVIDUAL-WATER", InitialWaterMeterValue = 10, RegisteredOn = month };
        var individual = new Tariff
        {
            Name = "Индивидуальная вода",
            CalculationBase = TariffCalculationBases.MeterWater,
            Rate = 20,
            EffectiveFrom = month
        };
        var assignment = new GarageTariffAssignment
        {
            Garage = garage,
            GarageId = garage.Id,
            ChargeServiceSetting = setting,
            ChargeServiceSettingId = setting.Id,
            Tariff = individual,
            TariffId = individual.Id,
            EffectiveFrom = month
        };
        context.Add(assignment);
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context);
        var reading = await service.SavePaymentFormMeterReadingAsync(new(garage.Id, MeterKinds.Water, month, month.AddDays(19), 15.5m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == water.Id);
        Assert.Equal(110m, accrual.Amount);
        var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(month, month), CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(110m, Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == water.Id).AccrualAmount);
        individual.Rate = 999;
        await context.SaveChangesAsync();
        var correctedReading = await service.SavePaymentFormMeterReadingAsync(
            new(garage.Id, MeterKinds.Water, month, month.AddDays(19), 16.5m, null, reading.Value!.Id, reading.Value.Version), null, CancellationToken.None);
        Assert.True(correctedReading.Succeeded, correctedReading.ErrorMessage);
        // Reading correction uses the issued snapshot, not a subsequently edited tariff.
        Assert.Equal(130m, accrual.Amount);
        assignment.IsArchived = true;
        await context.SaveChangesAsync();
        var general = await service.CalculateGarageIncomeWorksheetAsync(garage.Id, new(month, month), null, CancellationToken.None);
        Assert.True(general.Succeeded, general.ErrorMessage);
        Assert.Equal(325m, Assert.Single(general.Value!.Rows, row => row.IncomeTypeId == water.Id).AccrualAmount);
    }

    [PostgreSqlFact]
    public async Task IndividualElectricityTiersOverrideTheFlatGeneralTariffOnlyForAssignedGarage()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var month = new DateOnly(2050, 9, 1);
        var garage = new Garage { Number = "85-INDIVIDUAL-TIERS", RegisteredOn = month };
        var income = new IncomeType { Name = "Электроэнергия отдельного счётчика", Code = "individual_electricity_test" };
        var general = new Tariff { Name = "Общая электроэнергия без ступеней", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 5, EffectiveFrom = month };
        var individual = new Tariff
        {
            Name = "Индивидуальные ступени",
            CalculationBase = TariffCalculationBases.MeterElectricity,
            Rate = 1,
            EffectiveFrom = month,
            ElectricityFirstThreshold = 100,
            ElectricitySecondThreshold = 200,
            ElectricityFirstRate = 1,
            ElectricitySecondRate = 3,
            ElectricityThirdRate = 4
        };
        var setting = new ChargeServiceSetting
        {
            Name = income.Name,
            IncomeType = income,
            Tariff = general,
            IsRegular = true,
            IsMetered = true,
            HasTieredTariff = false,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            UnitName = "кВт·ч"
        };
        setting.MeterKind = MeterKinds.ForService(setting.Id);
        context.AddRange(new GarageTariffAssignment
        {
            Garage = garage,
            GarageId = garage.Id,
            ChargeServiceSetting = setting,
            ChargeServiceSettingId = setting.Id,
            Tariff = individual,
            TariffId = individual.Id,
            EffectiveFrom = month
        }, new MeterReading
        {
            Garage = garage,
            GarageId = garage.Id,
            MeterKind = setting.MeterKind,
            AccountingMonth = month,
            ReadingDate = month.AddDays(19),
            PreviousValue = 0,
            CurrentValue = 150,
            Consumption = 150
        });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context,
            new FixedTimeProvider(new DateTimeOffset(2050, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        var result = await service.GenerateRegularAccrualsAsync(new(income.Id, general.Id, month, null), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(row => row.GarageId == garage.Id && row.IncomeTypeId == income.Id);
        Assert.Equal(250m, accrual.Amount);
        Assert.Contains("ступени", accrual.Comment);
        Assert.True(accrual.RequiresMeterReading);
        var snapshot = RegularAccrualCalculator.Deserialize(accrual.CalculationDetailsJson);
        Assert.NotNull(snapshot);
        Assert.Equal("metered_tiered", Assert.Single(snapshot.Lines).CalculationMode);
    }
}
