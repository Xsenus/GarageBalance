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
    public async Task StartValueChange_AuditsThousandthPrecision()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertThousandthPrecisionAuditAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_StartValueChangeAuditsThousandthPrecision()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertThousandthPrecisionAuditAsync(context);
    }

    private static async Task AssertThousandthPrecisionAuditAsync(GarageBalanceDbContext context)
    {
        var garage = Garage("precision-audit");
        garage.InitialWaterMeterValue = 10.101m;
        context.Garages.Add(garage);
        await context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(
            garage.Id, GarageRequest(garage, water: 10.102m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Equal(10.102m, (await context.Garages.SingleAsync()).InitialWaterMeterValue);
        var audit = Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.meter_start_value_changed").ToListAsync());
        Assert.Equal(garage.Id.ToString(), audit.EntityId);
        using var metadata = System.Text.Json.JsonDocument.Parse(audit.MetadataJson!);
        Assert.Contains("10.101", metadata.RootElement.GetProperty("oldValue").GetString(), StringComparison.Ordinal);
        Assert.Contains("10.102", metadata.RootElement.GetProperty("newValue").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateGarage_IgnoresCanceledReadingsWhenChangingStartValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "2", InitialElectricityMeterValue = 0m, InitialWaterMeterValue = 0m, InitialMeterReadingMonth = new DateOnly(2026, 9, 1) };
        context.Garages.Add(garage);
        context.MeterReadings.AddRange(
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Electricity, AccountingMonth = new DateOnly(2026, 8, 1), ReadingDate = new DateOnly(2026, 9, 18), PreviousValue = 1000m, CurrentValue = 1200m, Consumption = 200m, IsCanceled = true },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Water, AccountingMonth = new DateOnly(2026, 8, 1), ReadingDate = new DateOnly(2026, 9, 18), PreviousValue = 30m, CurrentValue = 32m, Consumption = 2m, IsCanceled = true });
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);

        var result = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, water: 32m, electricity: 1200m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var stored = await context.Garages.AsNoTracking().SingleAsync(item => item.Id == garage.Id);
        Assert.Equal(32m, stored.InitialWaterMeterValue);
        Assert.Equal(1200m, stored.InitialElectricityMeterValue);
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

        var saved = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: [new(commercial.MeterKind!, 5000m, October)]), null, CancellationToken.None);
        var values = await service.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None);

        Assert.True(saved.Succeeded, saved.ErrorMessage);
        var device = await context.MeterDevices.AsNoTracking().SingleAsync();
        Assert.Equal((commercial.MeterKind, 5000m), (device.MeterKind, device.InitialValue));
        Assert.Equal(5000m, Assert.Single(values.Value!).Value);
        Assert.Contains(await context.AuditEvents.AsNoTracking().ToListAsync(), item => item.Action == "finance.meter_connection_changed");
    }

    [Fact]
    public async Task NewGarage_OffersOnlyUnrestrictedMeteredServicesAndStoresTheirStartValues()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var other = Garage("9к");
        context.Garages.Add(other);
        AddMeteredService(context, "Вода", MeterKinds.Water, TariffCalculationBases.MeterWater);
        var snow = AddMeteredService(context, "Вывоз снега", MeterKinds.ForService(Guid.NewGuid()), TariffCalculationBases.MeterWater);
        var restricted = AddMeteredService(context, "Коммерческий тариф", MeterKinds.ForService(Guid.NewGuid()), TariffCalculationBases.MeterElectricity, restrictedTo: other.Id);
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var offered = await service.GetMeterStartServicesForNewGarageAsync(CancellationToken.None);
        var created = await service.CreateGarageAsync(
            new UpsertGarageRequest("20", 1, 1, null, 0m, null, null, null, MeterStartValues:
                [new("water", 12m, October), new(snow.MeterKind!, 340m, October)]), null, CancellationToken.None);
        var rejected = await service.CreateGarageAsync(
            new UpsertGarageRequest("21", 1, 1, null, 0m, null, null, null, MeterStartValues: [new(restricted.MeterKind!, 5m)]), null, CancellationToken.None);

        Assert.Equal(["Вода", "Вывоз снега"], offered.Select(item => item.ServiceName).ToArray());
        Assert.All(offered, item => Assert.Null(item.Value));
        Assert.True(created.Succeeded, created.ErrorMessage);
        context.ChangeTracker.Clear();
        var stored = await context.Garages.AsNoTracking().SingleAsync(item => item.Number == "20");
        Assert.Null(stored.InitialWaterMeterValue);
        Assert.Equal(12m, (await context.MeterDevices.AsNoTracking().SingleAsync(item => item.GarageId == stored.Id && item.MeterKind == MeterKinds.Water)).InitialValue);
        var device = await context.MeterDevices.AsNoTracking().SingleAsync(item => item.GarageId == stored.Id && item.MeterKind == snow.MeterKind);
        Assert.Equal((snow.MeterKind, 340m), (device.MeterKind, device.InitialValue));
        Assert.Equal("meter_start_service_not_applicable", rejected.ErrorCode);
        Assert.DoesNotContain(await context.Garages.AsNoTracking().ToListAsync(), item => item.Number == "21");
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
        Assert.Equal(1500m, readingBefore.Consumption);
        Assert.Equal(15000m, accrualBefore.Amount);

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 1100m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var reading = await context.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal((100m, 400m), (reading.PreviousDeviceConsumption, reading.Consumption));
        var boundary = await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"));
        Assert.Equal(1100m, boundary.InitialValue);
        var legacy = await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber == "Без номера");
        Assert.Equal(1000m, legacy.InitialValue);
        var accrual = await context.Accruals.AsNoTracking().SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled);
        Assert.Equal(4000m, accrual.Amount);
        var values = await dictionary.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None);
        Assert.Equal(1100m, Assert.Single(values.Value!, item => item.MeterKind == MeterKinds.Electricity).Value);
        Assert.Contains(await context.AuditEvents.AsNoTracking().ToListAsync(), item => item.Action == "finance.meter_start_value_changed");
    }

    [Fact]
    public async Task UpdateGarage_RejectsStartValueAboveFinalValueOfReplacedDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _) = await SeedReplacedElectricityMeterAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 1300m), null, CancellationToken.None);

        Assert.Equal("meter_start_above_final", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(0m, (await context.Garages.AsNoTracking().SingleAsync()).InitialElectricityMeterValue);
        Assert.Equal(0m, (await context.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"))).InitialValue);
    }

    [Fact]
    public async Task UpdateGarage_RecalculatesPaidAccrualOnStartValueChangeAndKeepsOverpaymentAsAdvance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var accrual = await context.Accruals.SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled);
        Assert.Equal(15000m, accrual.Amount);
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = new DateOnly(2026, 10, 9),
            AccountingMonth = October,
            Amount = 5000m,
            GarageId = garage.Id,
            IncomeTypeId = income.Id
        };
        context.Add(payment);
        context.Add(new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = 5000m, IsActive = true });
        await context.SaveChangesAsync();
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 1100m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var reading = await context.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal((100m, 400m), (reading.PreviousDeviceConsumption, reading.Consumption));
        Assert.Equal(4000m, (await context.Accruals.AsNoTracking().SingleAsync(item => item.Id == accrual.Id)).Amount);
        // The payment is kept as is; the part above the reduced accrual becomes an unallocated advance.
        var storedPayment = await context.FinancialOperations.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
        Assert.False(storedPayment.IsCanceled);
        Assert.Equal(5000m, storedPayment.Amount);
        var allocations = await context.AccrualPaymentAllocations.AsNoTracking().Where(item => item.IsActive).ToListAsync();
        Assert.Equal(4000m, allocations.Where(item => item.AccrualId == accrual.Id).Sum(item => item.Amount));
        Assert.Equal(1000m, storedPayment.Amount - allocations.Where(item => item.FinancialOperationId == payment.Id).Sum(item => item.Amount));
    }

    [Fact]
    public async Task GetMeterDevices_ReportsLastActiveReadingOfEachDevice()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _) = await SeedReplacedElectricityMeterAsync(context);
        var finance = FinanceServiceTestFactory.Create(context, new FixedTime(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero)));

        var devices = await finance.GetMeterDevicesAsync(garage.Id, MeterKinds.Electricity, CancellationToken.None);

        Assert.Equal(1500m, Assert.Single(devices, item => item.SerialNumber == "1").LastReadingValue);
        Assert.All(devices.Where(item => item.SerialNumber != "1"), item => Assert.Null(item.LastReadingValue));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RecalculatesReplacementChainAndAccrualWhenStartValueIsSet()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 10, 10));

        var result = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 1100m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verify = database.CreateContext();
        var reading = await verify.MeterReadings.AsNoTracking().SingleAsync(item => item.IsMeterReplacement);
        Assert.Equal((100m, 400m), (reading.PreviousDeviceConsumption, reading.Consumption));
        Assert.Equal(4000m, (await verify.Accruals.AsNoTracking().SingleAsync(item => item.IncomeTypeId == income.Id && !item.IsCanceled)).Amount);
        Assert.Equal(1100m, (await verify.MeterDevices.AsNoTracking().SingleAsync(item => item.SerialNumber.StartsWith("Начало учёта"))).InitialValue);
    }

    [Fact]
    public async Task UpdateGarage_SavesLegacyColumnWhenItAlreadyMatchesTheDeviceBaseline()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = Garage("same-baseline");
        garage.InitialWaterMeterValue = 0m;
        database.Context.Add(garage);
        database.Context.Add(new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            SerialNumber = "Начало учёта",
            InstalledOn = October,
            InitialValue = 10m
        });
        await database.Context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(database.Context).UpdateGarageAsync(
            garage.Id, GarageRequest(garage, water: 10m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(10m, (await database.Context.Garages.SingleAsync()).InitialWaterMeterValue);
        Assert.Equal(10m, (await database.Context.MeterDevices.SingleAsync()).InitialValue);
        Assert.Contains(await database.Context.AuditEvents.ToListAsync(), item => item.Action == "dictionary.garage_updated");
    }

    [Theory]
    [InlineData("water", " water ", "meter_start_kind_duplicate")]
    [InlineData("electricity", "electricity", "meter_start_kind_duplicate")]
    [InlineData("water", "unknown", "meter_kind_invalid")]
    [InlineData("water", null, "meter_kind_invalid")]
    public async Task GarageSave_RejectsDuplicateAndInvalidMeterKindsBeforeChangingData(string first, string? second, string errorCode)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = Garage("validation");
        database.Context.Add(garage);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        UpsertGarageMeterStartValueRequest[] values = [new(first, 10m), new(second!, 20m)];

        var created = await service.CreateGarageAsync(new("new-validation", 1, 1, null, 0m, null, null, null, MeterStartValues: values), null, CancellationToken.None);
        var updated = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: values), null, CancellationToken.None);

        Assert.Equal(errorCode, created.ErrorCode);
        Assert.Equal(errorCode, updated.ErrorCode);
        Assert.DoesNotContain(database.Context.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
        Assert.Equal(1, await database.Context.Garages.CountAsync());
    }

    [Fact]
    public async Task GarageSave_RejectsNullListEntryWithoutChangingData()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = Garage("null-entry");
        database.Context.Add(garage);
        await database.Context.SaveChangesAsync();
        var dictionary = DictionaryServiceTestFactory.Create(database.Context);
        UpsertGarageMeterStartValueRequest[] values = [null!];

        var created = await dictionary.CreateGarageAsync(new("new-null", 1, 1, null, 0m, null, null, null, MeterStartValues: values), null, CancellationToken.None);
        var updated = await dictionary.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: values), null, CancellationToken.None);
        var applied = await FinanceServiceTestFactory.Create(database.Context).ApplyStartValuesAsync(garage, values, null, CancellationToken.None);

        Assert.Equal("meter_kind_invalid", created.ErrorCode);
        Assert.Equal("meter_kind_invalid", updated.ErrorCode);
        Assert.Equal("meter_kind_invalid", applied.ErrorCode);
        Assert.DoesNotContain(database.Context.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
    }

    [Theory]
    [InlineData("unknown", 10, "meter_kind_invalid")]
    [InlineData("water", -1, "meter_start_value_invalid")]
    [InlineData("water", 1000000000, "meter_start_value_invalid")]
    [InlineData(" water ", 20, "meter_start_kind_duplicate")]
    public async Task StartValueApplication_ValidatesAllRequestsBeforeMutation(string kind, int value, string errorCode)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = Garage("direct-validation");
        database.Context.Add(garage);
        await database.Context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(database.Context);

        var result = await finance.ApplyStartValuesAsync(garage, [new("water", 10m), new(kind, value)], null, CancellationToken.None);

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Null(garage.InitialWaterMeterValue);
        Assert.DoesNotContain(database.Context.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000000000)]
    public async Task GarageSave_RejectsOutOfRangeServiceStartValues(int value)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = Garage("range");
        database.Context.Add(garage);
        await database.Context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(database.Context);
        UpsertGarageMeterStartValueRequest[] values = [new(MeterKinds.Water, value)];

        var created = await service.CreateGarageAsync(new("new-range", 1, 1, null, 0m, null, null, null, MeterStartValues: values), null, CancellationToken.None);
        var updated = await service.UpdateGarageAsync(garage.Id, GarageRequest(garage, startValues: values), null, CancellationToken.None);

        Assert.Equal("meter_start_value_invalid", created.ErrorCode);
        Assert.Equal("meter_start_value_invalid", updated.ErrorCode);
        Assert.Empty(await database.Context.MeterDevices.ToListAsync());
    }

    [Fact]
    public async Task UpdateGarage_DoesNotBlockOrChangePaidLaterMonthWhoseConsumptionIsUnchanged()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertUnchangedPaidMonthAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_StartValueChangePreservesUnchangedPaidMonth()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertUnchangedPaidMonthAsync(context);
    }

    private static async Task AssertUnchangedPaidMonthAsync(GarageBalanceDbContext context)
    {
        var (garage, income) = await SeedReplacedElectricityMeterAsync(context);
        var november = October.AddMonths(1);
        var finance = FinanceServiceTestFactory.Create(context,
            new FixedTime(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero)), businessDate: november);
        var later = await finance.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity, november, november.AddDays(9), 1510m, null), null, CancellationToken.None);
        Assert.True(later.Succeeded, later.ErrorMessage);
        var accrual = await context.Accruals.SingleAsync(item => item.AccountingMonth == november && !item.IsCanceled && item.IncomeTypeId == income.Id);
        await context.Entry(accrual).ReloadAsync();
        var originalDetails = accrual.CalculationDetailsJson;
        var payment = new FinancialOperation
        {
            OperationKind = FinancialOperationKinds.Income,
            OperationDate = november.AddDays(9),
            AccountingMonth = november,
            GarageId = garage.Id,
            IncomeTypeId = income.Id,
            Amount = accrual.Amount,
            TargetAccrualId = accrual.Id
        };
        context.Add(payment);
        context.Add(new AccrualPaymentAllocation { Accrual = accrual, FinancialOperation = payment, Amount = accrual.Amount, IsActive = true });
        await context.SaveChangesAsync();

        var result = await DictionaryServiceTestFactory.Create(context, new DateOnly(2026, 11, 10))
            .UpdateGarageAsync(garage.Id, GarageRequest(garage, electricity: 1100m), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var storedReading = await context.MeterReadings.SingleAsync(item => item.Id == later.Value!.Id);
        var storedAccrual = await context.Accruals.SingleAsync(item => item.Id == accrual.Id);
        Assert.Equal(10m, storedReading.Consumption);
        Assert.Equal(100m, storedAccrual.Amount);
        Assert.Equal(originalDetails, storedAccrual.CalculationDetailsJson);
        Assert.Equal(later.Value!.Version, storedReading.Version);
        Assert.Equal(100m, await context.AccrualPaymentAllocations.Where(item => item.AccrualId == accrual.Id && item.IsActive).SumAsync(item => item.Amount));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_StartValueLocksProtectSaveAndReleaseCanceledContender()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var firstContext = database.CreateContext();
        var garage = Garage("locks");
        garage.InitialWaterMeterValue = 1m;
        garage.InitialElectricityMeterValue = 1m;
        firstContext.Add(garage);
        await firstContext.SaveChangesAsync();
        var first = FinanceServiceTestFactory.Create(firstContext);
        var applied = await first.ApplyStartValuesAsync(garage, [new("water", 10m), new("electricity", 20m)], null, CancellationToken.None);
        Assert.True(applied.Succeeded, applied.ErrorMessage);

        await using var secondContext = database.CreateContext();
        var secondGarage = await secondContext.Garages.SingleAsync(item => item.Id == garage.Id);
        var second = FinanceServiceTestFactory.Create(secondContext);
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.ApplyStartValuesAsync(secondGarage,
                [new("electricity", 30m), new("water", 15m)], null, cancellation.Token));
            Assert.DoesNotContain(secondContext.ChangeTracker.Entries(), entry => entry.State is EntityState.Added or EntityState.Modified);
            await firstContext.SaveChangesAsync();
        }
        finally
        {
            await applied.Value!.DisposeAsync();
        }

        await secondContext.Entry(secondGarage).ReloadAsync();
        var retried = await second.ApplyStartValuesAsync(secondGarage, [new("electricity", 30m), new("water", 15m)], null, CancellationToken.None);
        Assert.True(retried.Succeeded, retried.ErrorMessage);
        await using (retried.Value!)
        {
            await secondContext.SaveChangesAsync();
        }
        await using var verify = database.CreateContext();
        var stored = await verify.Garages.SingleAsync(item => item.Id == garage.Id);
        Assert.Equal((15m, 30m), (stored.InitialWaterMeterValue, stored.InitialElectricityMeterValue));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_LastDeviceReadingsExcludeCanceledOtherGaragesAndKinds()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertLastDeviceReadingsAsync(context);
    }

    [Fact]
    public async Task LastDeviceReadingsExcludeCanceledOtherGaragesAndKinds()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertLastDeviceReadingsAsync(database.Context);
    }

    private static async Task AssertLastDeviceReadingsAsync(GarageBalanceDbContext context)
    {
        var garage = Garage("latest");
        var other = Garage("other");
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Water, SerialNumber = "W", InstalledOn = October };
        context.AddRange(garage, other, device);
        context.MeterReadings.AddRange(
            new MeterReading { Garage = garage, MeterDevice = device, MeterKind = MeterKinds.Water, AccountingMonth = October, ReadingDate = October, CurrentValue = 100m },
            new MeterReading { Garage = garage, MeterDevice = device, MeterKind = MeterKinds.Water, AccountingMonth = October.AddMonths(1), ReadingDate = October.AddMonths(1), CurrentValue = 120m },
            new MeterReading { Garage = garage, MeterDevice = device, MeterKind = MeterKinds.Water, AccountingMonth = October.AddMonths(2), ReadingDate = October.AddMonths(2), CurrentValue = 900m, IsCanceled = true },
            new MeterReading { Garage = garage, MeterKind = MeterKinds.Electricity, AccountingMonth = October, ReadingDate = October, CurrentValue = 800m },
            new MeterReading { Garage = other, MeterKind = MeterKinds.Water, AccountingMonth = October, ReadingDate = October, CurrentValue = 700m });
        await context.SaveChangesAsync();
        var repository = new EfMeterReadingRepository(context);

        var values = await repository.GetLastReadingValuesByDeviceAsync(garage.Id, MeterKinds.Water, CancellationToken.None);

        Assert.Equal(120m, Assert.Single(values).Value);
        Assert.Equal(device.Id, Assert.Single(values).Key);
        Assert.Empty(await repository.GetLastReadingValuesByDeviceAsync(other.Id, MeterKinds.Water, CancellationToken.None));
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
            new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "Без номера", InstalledOn = new DateOnly(2026, 9, 18), RemovedOn = new DateOnly(2026, 9, 30), InitialValue = 1000m, FinalValue = 1200m },
            new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "Начало учёта 01.10.2026", InstalledOn = October, InitialValue = 0m });
        await context.SaveChangesAsync();

        var finance = FinanceServiceTestFactory.Create(context, new FixedTime(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)), businessDate: new DateOnly(2026, 10, 9));
        var replaced = await finance.ReplaceMeterDeviceAsync(new ReplaceMeterDeviceRequest(
            garage.Id, MeterKinds.Electricity, October, new DateOnly(2026, 10, 9), "1", 1200m, 1500m, 1200m, "Проверка"), null, CancellationToken.None);
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
