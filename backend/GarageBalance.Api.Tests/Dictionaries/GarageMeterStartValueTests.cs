using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageMeterStartValueTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    [Fact]
    public async Task UpdateGarage_IgnoresCanceledReadingsWhenChangingStartValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "2", InitialElectricityMeterValue = 0m, InitialWaterMeterValue = 0m, InitialMeterReadingMonth = new DateOnly(2026, 9, 1) };
        context.Garages.Add(garage);
        context.MeterReadings.AddRange(
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Electricity, AccountingMonth = new DateOnly(2026, 8, 1), ReadingDate = new DateOnly(2026, 9, 18), PreviousValue = 119153m, CurrentValue = 124626m, Consumption = 5473m, IsCanceled = true },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = new DateOnly(2026, 8, 1), ReadingDate = new DateOnly(2026, 9, 18), PreviousValue = 296m, CurrentValue = 320m, Consumption = 24m, IsCanceled = true });
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var result = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, water: 320m, electricity: 124626m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var stored = await context.Garages.AsNoTracking().SingleAsync(item => item.Id == garage.Id);
        Assert.Equal(320m, stored.InitialWaterMeterValue);
        Assert.Equal(124626m, stored.InitialElectricityMeterValue);
    }

    [Fact]
    public async Task UpdateGarage_RejectsStartValueAboveExistingReadingWithoutChangingGarage()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "3", InitialWaterMeterValue = 10m };
        context.Garages.Add(garage);
        context.MeterReadings.Add(new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = October, ReadingDate = October, PreviousValue = 10m, CurrentValue = 12m, Consumption = 2m });
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var result = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, water: 13m), null, CancellationToken.None);

        Assert.Equal("meter_start_value_breaks_readings", result.ErrorCode);
        context.ChangeTracker.Clear();
        var stored = await context.Garages.AsNoTracking().SingleAsync(item => item.Id == garage.Id);
        var reading = await context.MeterReadings.AsNoTracking().SingleAsync();
        Assert.Equal(10m, stored.InitialWaterMeterValue);
        Assert.Equal((10m, 2m), (reading.PreviousValue, reading.Consumption));
    }

    [Fact]
    public async Task GetStartValues_ListsOnlyMeteredServicesApplicableToGarage()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var commercial = Garage("1к");
        var regular = Garage("1");
        context.Garages.AddRange(commercial, regular);
        var water = AddMeteredService(context, "Вода", MeterKinds.Water, TariffCalculationBases.MeterWater);
        var electricity = AddMeteredService(context, "Электроэнергия", MeterKinds.Electricity, TariffCalculationBases.MeterElectricity, restrictedTo: regular.Id);
        var commercialService = AddMeteredService(context, "Коммерческий тариф", MeterKinds.ForService(Guid.NewGuid()), TariffCalculationBases.MeterElectricity, restrictedTo: commercial.Id);
        context.ChargeServiceSettings.Add(new ChargeServiceSetting { Name = "Членский взнос", IsRegular = true, IsMetered = false });
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var commercialValues = await service.GetGarageMeterStartValuesAsync(commercial.Id, CancellationToken.None);
        var regularValues = await service.GetGarageMeterStartValuesAsync(regular.Id, CancellationToken.None);
        var missing = await service.GetGarageMeterStartValuesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(["Вода", "Коммерческий тариф"], commercialValues.Value!.Select(item => item.ServiceName).Order(StringComparer.CurrentCulture).ToArray());
        Assert.Contains(commercialValues.Value!, item => item.MeterKind == commercialService.MeterKind);
        Assert.DoesNotContain(commercialValues.Value!, item => item.MeterKind == MeterKinds.Electricity);
        Assert.Equal(["Вода", "Электроэнергия"], regularValues.Value!.Select(item => item.ServiceName).Order(StringComparer.CurrentCulture).ToArray());
        Assert.Equal("garage_not_found", missing.ErrorCode);
        Assert.NotNull(water);
        Assert.NotNull(electricity);
    }

    [Fact]
    public async Task UpdateGarage_StoresServiceMeterStartValueAsBaselineDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = Garage("1к");
        context.Garages.Add(garage);
        var commercial = AddMeteredService(context, "Коммерческий тариф", MeterKinds.ForService(Guid.NewGuid()), TariffCalculationBases.MeterElectricity, restrictedTo: garage.Id);
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var saved = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: [new(commercial.MeterKind!, 5000m)]), null, CancellationToken.None);
        var values = await service.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None);

        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var device = await context.MeterDevices.AsNoTracking().SingleAsync();
        Assert.Equal((commercial.MeterKind, 5000m), (device.MeterKind, device.InitialValue));
        Assert.Equal(5000m, Assert.Single(values.Value!).Value);
        Assert.Contains(await context.AuditEvents.AsNoTracking().ToListAsync(), item => item.Action == "finance.meter_start_value_changed");
    }

    [Fact]
    public async Task UpdateGarage_RejectsServiceMeterNotApplicableToGarage()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = Garage("5");
        var other = Garage("6к");
        context.Garages.AddRange(garage, other);
        var commercial = AddMeteredService(context, "Коммерческий тариф", MeterKinds.ForService(Guid.NewGuid()), TariffCalculationBases.MeterElectricity, restrictedTo: other.Id);
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var result = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: [new(commercial.MeterKind!, 10m)]), null, CancellationToken.None);

        Assert.Equal("meter_start_service_not_applicable", result.ErrorCode);
        Assert.Empty(await context.MeterDevices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UpdateGarage_RecalculatesReplacementChainAndAccrualWhenStartValueIsSet()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));
        var accrualBefore = await context.Accruals.AsNoTracking().SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled);
        var readingBefore = await context.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal(129509m, readingBefore.Consumption);
        Assert.Equal(1295090m, accrualBefore.Amount);

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 124000m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var reading = await context.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal((626m, 5509m), (reading.PreviousDeviceConsumption, reading.Consumption));
        var boundary = await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"));
        Assert.Equal(124000m, boundary.InitialValue);
        var legacy = await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber == "Без номера");
        Assert.Equal(119153m, legacy.InitialValue);
        var accrual = await context.Accruals.AsNoTracking().SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled);
        Assert.Equal(55090m, accrual.Amount);
        var values = await dictionary.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None);
        Assert.Equal(124000m, Assert.Single(values.Value!, item => item.MeterKind == MeterKinds.Electricity).Value);
        Assert.Contains(await context.AuditEvents.AsNoTracking().ToListAsync(), item => item.Action == "finance.meter_start_value_changed");
    }

    [Fact]
    public async Task UpdateGarage_RejectsStartValueAboveFinalValueOfReplacedDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _) = await SeedReplacedElectricityMeterAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 125000m), null, CancellationToken.None);

        Assert.Equal("meter_start_above_final", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(0m, (await context.Garages.AsNoTracking().SingleAsync()).InitialElectricityMeterValue);
        Assert.Equal(0m, (await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"))).InitialValue);
    }

    [Fact]
    public async Task UpdateGarage_BlocksStartValueChangeWhenAffectedAccrualIsPaid()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var accrual = await context.Accruals.SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled);
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 10, 9),
            AccountingMonth = October,
            Amount = 100m,
            GarageId = garage.Id,
            IncomeTypeId = income.Id
        };
        context.Add(payment);
        context.Add(new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = 100m, IsActive = true });
        await context.SaveChangesAsync();
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 124000m), null, CancellationToken.None);

        Assert.Equal("meter_start_accrual_paid", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(129509m, (await context.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement)).Consumption);
    }

    [Fact]
    public async Task GetMeterDevices_ReportsLastActiveReadingOfEachDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _) = await SeedReplacedElectricityMeterAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, new FixedTime(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)));

        var devices = await finance.GetMeterDevicesAsync(garage.Id, MeterKinds.Electricity, CancellationToken.None);

        Assert.Equal(129509m, Assert.Single(devices, item => item.SerialNumber == "1").LastReadingValue);
        Assert.All(devices.Where(item => item.SerialNumber != "1"), item => Assert.Null(item.LastReadingValue));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RecalculatesReplacementChainAndAccrualWhenStartValueIsSet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 124000m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verify = database.CreateContext();
        var reading = await verify.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal((626m, 5509m), (reading.PreviousDeviceConsumption, reading.Consumption));
        Assert.Equal(55090m, (await verify.Accruals.AsNoTracking().SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled)).Amount);
        Assert.Equal(124000m, (await verify.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"))).InitialValue);
    }

    private static async Task<(Garage Garage, IncomeType Income)> SeedReplacedElectricityMeterAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage { Number = "2", InitialElectricityMeterValue = 0m, InitialMeterReadingMonth = new DateOnly(2026, 9, 1) };
        var income = new IncomeType { Name = "Счётчик со стартовым значением", Code = "electricity_start" };
        var tariff = new Tariff { Name = "Тариф со стартовым значением", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 10m, EffectiveFrom = new DateOnly(2026, 1, 1) };
        context.AddRange(garage, income, tariff);
        context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Услуга со стартовым значением",
            IncomeType = income,
            Tariff = tariff,
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.Electricity,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            UnitName = "кВт·ч"
        });
        context.MeterDevices.AddRange(
            new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "Без номера", InstalledOn = new DateOnly(2026, 9, 18), RemovedOn = new DateOnly(2026, 9, 30), InitialValue = 119153m, FinalValue = 124626m },
            new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "Начало учёта 01.10.2026", InstalledOn = October, InitialValue = 0m });
        await context.SaveChangesAsync();

        var finance = FinanceServiceTestFactory.Create(context, new FixedTime(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)), businessDate: new DateOnly(2026, 10, 9));
        var replaced = await finance.ReplaceMeterDeviceAsync(new ReplaceMeterDeviceRequest(
            garage.Id, MeterKinds.Electricity, October, new DateOnly(2026, 10, 9), "1", 124626m, 129509m, 124626m, "Проверка"), null, CancellationToken.None);
        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        context.ChangeTracker.Clear();
        return (await context.Garages.SingleAsync(item => item.Id == garage.Id), income);
    }

    private static UpsertGarageRequest GarageRequest(
        Garage garage,
        decimal? water = null,
        decimal? electricity = null,
        IReadOnlyList<UpsertGarageMeterStartValueRequest>? startValues = null) =>
        new(garage.Number, garage.PeopleCount, garage.FloorCount, garage.OwnerId, garage.StartingBalance,
            water ?? garage.InitialWaterMeterValue, electricity ?? garage.InitialElectricityMeterValue, garage.Comment,
            garage.Version, garage.StartingOverdueDebt, startValues);

    private static Garage Garage(string number) =>
        new() { Number = number, InitialMeterReadingMonth = new DateOnly(2026, 9, 1) };

    private static ChargeServiceSetting AddMeteredService(
        GarageBalanceDbContext context,
        string name,
        string meterKind,
        string calculationBase,
        Guid? restrictedTo = null)
    {
        var setting = new ChargeServiceSetting
        {
            Name = name,
            IsRegular = true,
            IsMetered = true,
            MeterKind = meterKind,
            UnitName = "кВт·ч",
            AppliesToSelectedGarages = restrictedTo.HasValue,
            GarageIds = restrictedTo.HasValue ? [restrictedTo.Value] : []
        };
        context.ChargeServiceSettings.Add(setting);
        _ = calculationBase;
        return setting;
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
