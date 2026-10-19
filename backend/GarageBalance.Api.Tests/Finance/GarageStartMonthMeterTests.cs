using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

// Customer comments 14.10.2026, item 3: the start value belongs to the month before the
// garage's "Учитывать с" month, and the operator corrects it without a separate meter date.
public sealed class GarageStartMonthMeterTests
{
    private static readonly DateOnly August = new(2026, 8, 1);
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public async Task Sqlite_StartValueFollowsGarageStartAndRecalculatesConsumption()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCustomerExampleAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_StartValueFollowsGarageStartAndRecalculatesConsumption()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCustomerExampleAsync(context);
    }

    private static async Task AssertCustomerExampleAsync(GarageBalanceDbContext context)
    {
        var setting = await SeedElectricityAsync(context);
        var dictionaries = DictionaryServiceTestFactory.Create(context, Today);
        var created = await dictionaries.CreateGarageAsync(Request(September) with
        {
            MeterStartValues = [new UpsertGarageMeterStartValueRequest(setting.MeterKind!, 320)]
        }, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garageId = created.Value!.Id;
        var connection = await context.GarageMeterConnections.AsNoTracking().SingleAsync(item => item.GarageId == garageId);
        Assert.Equal(September, connection.ConnectedOn);

        var finance = FinanceServiceTestFactory.Create(context, businessDate: Today);
        var reading = await finance.CreateMeterReadingAsync(
            new(garageId, setting.MeterKind!, September, September.AddDays(29), 342, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(22m, reading.Value!.Consumption);
        Assert.Equal(110m, await CalculateSeptemberAsync(finance, garageId, setting));

        context.ChangeTracker.Clear();
        var corrected = await CreateConnectionService(context).SetStartValueAsync(
            new(garageId, setting.MeterKind!, 330), null, CancellationToken.None);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        var change = Assert.Single(corrected.Value!.Changes);
        Assert.Equal(320m, change.OldValue);
        Assert.Equal(330m, change.NewValue);
        context.ChangeTracker.Clear();
        Assert.Equal(12m, (await context.MeterReadings.AsNoTracking().SingleAsync(item => item.GarageId == garageId)).Consumption);
        Assert.Equal(60m, (await context.Accruals.AsNoTracking()
            .SingleAsync(item => item.GarageId == garageId && item.IncomeTypeId == setting.IncomeTypeId && !item.IsCanceled)).Amount);

        var garage = await context.Garages.AsNoTracking().SingleAsync(item => item.Id == garageId);
        var moved = await DictionaryServiceTestFactory.Create(context, Today).UpdateGarageAsync(garageId,
            Request(August) with { Version = garage.Version, AccountingStartReason = "Начало учёта перенесено" }, null, CancellationToken.None);
        Assert.True(moved.Succeeded, moved.ErrorMessage);
        context.ChangeTracker.Clear();
        connection = await context.GarageMeterConnections.AsNoTracking().SingleAsync(item => item.GarageId == garageId);
        Assert.Equal(August, connection.ConnectedOn);
        Assert.Equal(330m, (await context.MeterDevices.AsNoTracking().SingleAsync(item => item.Id == connection.BaselineMeterDeviceId)).InitialValue);
    }

    // Customer comments 14.10.2026, item 5: month start 270, removed at 300, new device continues.
    [Theory]
    [InlineData(500, 550, 80)]
    [InlineData(300, 330, 60)]
    [InlineData(250, 310, 90)]
    public async Task ReplacementCountsBothDevicesWithoutTheGapAndTwiceForAnOverlap(int newInitial, int monthEnd, int expected)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var setting = await SeedElectricityAsync(context);
        var created = await DictionaryServiceTestFactory.Create(context, Today).CreateGarageAsync(Request(September) with
        {
            MeterStartValues = [new UpsertGarageMeterStartValueRequest(setting.MeterKind!, 250)]
        }, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var garageId = created.Value!.Id;
        var finance = FinanceServiceTestFactory.Create(context, businessDate: Today);
        var september = await finance.CreateMeterReadingAsync(
            new(garageId, setting.MeterKind!, September, September.AddDays(29), 270, null), null, CancellationToken.None);
        Assert.True(september.Succeeded, september.ErrorMessage);

        var replaced = await finance.ReplaceMeterDeviceAsync(new(garageId, setting.MeterKind!, October, October.AddDays(9),
            "NEW-1", newInitial, monthEnd, 300, "Замена счётчика"), null, CancellationToken.None);

        Assert.True(replaced.Succeeded, replaced.ErrorMessage);
        Assert.Equal(expected, replaced.Value!.Reading.Consumption);
        var worksheet = await finance.CalculateGarageIncomeWorksheetAsync(garageId, new(October, October), null, CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        Assert.Equal(expected * 5m, Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == setting.IncomeTypeId).AccrualAmount);
    }

    [Fact]
    public async Task StartValueMovesALaterConnectionToTheGarageStart()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var setting = await SeedElectricityAsync(context);
        var garage = new Garage { Number = "SYNTHETIC-LATE", RegisteredOn = Today, AccountingStartMonth = September, PeopleCount = 1 };
        var device = new MeterDevice
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial,
            InstalledOn = Today,
            InitialValue = 320
        };
        context.AddRange(garage, device, new GarageMeterConnection
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            ConnectedOn = Today,
            BaselineMeterDevice = device
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await CreateConnectionService(context).SetStartValueAsync(new(garage.Id, setting.MeterKind!, 320), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var change = Assert.Single(result.Value!.Changes);
        Assert.Equal(Today, change.OldConnectedOn);
        Assert.Equal(September, change.NewConnectedOn);
        context.ChangeTracker.Clear();
        Assert.Equal(September, (await context.GarageMeterConnections.SingleAsync()).ConnectedOn);
        Assert.Equal(September, (await context.MeterDevices.SingleAsync()).InstalledOn);
        Assert.Contains(await context.AuditEvents.ToListAsync(), item => item.Action == "finance.meter_connection_changed");
    }

    // Garages after a meter accounting restart keep the restart boundary as their baseline device.
    [Fact]
    public async Task Sqlite_RestartBoundaryDefinesTheBaseMonthAndItsValueIsCorrectedWithoutAConnection()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRestartBoundaryAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RestartBoundaryDefinesTheBaseMonthAndItsValueIsCorrectedWithoutAConnection()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRestartBoundaryAsync(context);
    }

    private static async Task AssertRestartBoundaryAsync(GarageBalanceDbContext context)
    {
        var setting = await SeedElectricityAsync(context);
        var garage = new Garage { Number = "SYNTHETIC-RESTART", RegisteredOn = new(2025, 1, 1), AccountingStartMonth = new(2026, 1, 1), PeopleCount = 1, InitialElectricityMeterValue = 0, InitialMeterReadingMonth = September };
        var removed = new MeterDevice { Garage = garage, MeterKind = setting.MeterKind!, SerialNumber = "OLD-1", InstalledOn = new(2025, 1, 1), InitialValue = 10, RemovedOn = September.AddMonths(1).AddDays(-1), FinalValue = 90 };
        var boundary = new MeterDevice { Garage = garage, MeterKind = setting.MeterKind!, SerialNumber = $"{MeterBaselineResolver.RestartDeviceSerialPrefix} 01.10.2026", InstalledOn = October, InitialValue = 0 };
        context.AddRange(garage, removed, boundary,
            new MeterReading { Garage = garage, MeterKind = setting.MeterKind!, MeterDevice = removed, AccountingMonth = September, ReadingDate = September.AddDays(29), CurrentValue = 90, Consumption = 80, IsCanceled = true });
        await context.SaveChangesAsync();
        var finance = FinanceServiceTestFactory.Create(context, businessDate: Today);
        var reading = await finance.CreateMeterReadingAsync(new(garage.Id, setting.MeterKind!, October, October.AddDays(9), 120, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(120m, reading.Value!.Consumption);
        context.ChangeTracker.Clear();

        var start = Assert.Single(await finance.GetStartValuesAsync(await context.Garages.SingleAsync(item => item.Id == garage.Id), CancellationToken.None));
        Assert.Equal(October, start.BaselineStartsOn);
        Assert.Null(start.ConnectedOn);
        var year = Assert.Single((await new EfMeterReadingRepository(context).GetYearPageAsync(2026, setting.MeterKind!, 0, 25, CancellationToken.None)).Garages);
        Assert.Equal(("device", October, 0m), (year.StartValueSource, year.StartValueDate, year.StartValue));
        context.ChangeTracker.Clear();

        var corrected = await CreateConnectionService(context).SetStartValueAsync(
            new(garage.Id, setting.MeterKind!, 100, "Показание на конец сентября"), null, CancellationToken.None);

        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        Assert.Equal((0m, 100m), (Assert.Single(corrected.Value!.Changes).OldValue, corrected.Value.Changes[0].NewValue));
        context.ChangeTracker.Clear();
        Assert.False(await context.GarageMeterConnections.AnyAsync());
        var devices = await context.MeterDevices.AsNoTracking().OrderBy(item => item.InstalledOn).ToListAsync();
        Assert.Equal([(new DateOnly(2025, 1, 1), 10m), (October, 100m)], devices.Select(item => (item.InstalledOn, item.InitialValue)).ToArray());
        Assert.Equal(20m, (await context.MeterReadings.AsNoTracking().SingleAsync(item => !item.IsCanceled)).Consumption);
        Assert.Equal(100m, (await context.Accruals.AsNoTracking()
            .SingleAsync(item => item.GarageId == garage.Id && item.IncomeTypeId == setting.IncomeTypeId && !item.IsCanceled)).Amount);
        Assert.True(await context.MeterReadings.AnyAsync(item => item.IsCanceled && item.CurrentValue == 90));
    }

    [Fact]
    public async Task GarageCreatedWithoutStartMonthUsesItsRegistrationMonth()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var setting = await SeedElectricityAsync(database.Context);
        var created = await DictionaryServiceTestFactory.Create(database.Context, Today).CreateGarageAsync(Request(null) with
        {
            MeterStartValues = [new UpsertGarageMeterStartValueRequest(setting.MeterKind!, 15)]
        }, null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        Assert.Equal(October, (await database.Context.GarageMeterConnections.SingleAsync()).ConnectedOn);
        var services = await FinanceServiceTestFactory.Create(database.Context, businessDate: Today)
            .GetStartServicesForNewGarageAsync(CancellationToken.None);
        Assert.False(Assert.Single(services).RequiresConnectionDate);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("value")]
    [InlineData("reason")]
    [InlineData("garage")]
    [InlineData("missing")]
    public async Task SetStartValueRejectsInvalidInputAndMissingGarageWithoutChanges(string failure)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var setting = await SeedElectricityAsync(context);
        var garage = new Garage { Number = "SYNTHETIC-INVALID", RegisteredOn = Today, AccountingStartMonth = September };
        context.Add(garage);
        await context.SaveChangesAsync();
        SetGarageMeterStartValueRequest request = failure switch
        {
            "kind" => new(garage.Id, "unknown", 1),
            "value" => new(garage.Id, setting.MeterKind!, -1),
            "reason" => new(garage.Id, setting.MeterKind!, 1, new string('x', 1001)),
            "garage" => new(Guid.Empty, setting.MeterKind!, 1),
            _ => new(Guid.NewGuid(), setting.MeterKind!, 1)
        };

        var result = await CreateConnectionService(context).SetStartValueAsync(request, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(failure == "missing" ? "garage_not_found" : "meter_start_value_invalid", result.ErrorCode);
        Assert.False(await context.GarageMeterConnections.AnyAsync());
        Assert.False(await context.MeterDevices.AnyAsync());
    }

    private static async Task<decimal> CalculateSeptemberAsync(FinanceService finance, Guid garageId, ChargeServiceSetting setting)
    {
        var worksheet = await finance.CalculateGarageIncomeWorksheetAsync(garageId, new(September, September), null, CancellationToken.None);
        Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
        return Assert.Single(worksheet.Value!.Rows, row => row.IncomeTypeId == setting.IncomeTypeId).AccrualAmount;
    }

    private static GarageMeterConnectionService CreateConnectionService(GarageBalanceDbContext context) => new(
        new EfGarageRepository(context, new TestBusinessDateProvider(Today)), new EfMeterReadingRepository(context),
        FinanceServiceTestFactory.Create(context, businessDate: Today), new EfApplicationUnitOfWork(context));

    private static async Task<ChargeServiceSetting> SeedElectricityAsync(GarageBalanceDbContext context)
    {
        foreach (var existing in context.ChargeServiceSettings) existing.IsArchived = true;
        var setting = new ChargeServiceSetting
        {
            Name = "Synthetic start electricity",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.Electricity,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            OverdueGraceDays = 30,
            UnitName = "кВт·ч",
            IncomeType = new IncomeType { Name = "Synthetic start electricity", Code = "synthetic_start_electricity" },
            Tariff = new Tariff { Name = "Synthetic start electricity", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 5, EffectiveFrom = new(2026, 1, 1) }
        };
        context.Add(setting);
        await context.SaveChangesAsync();
        return setting;
    }

    private static UpsertGarageRequest Request(DateOnly? month) =>
        new("synthetic-start", 1, 1, null, 0, null, null, null, AccountingStartMonth: month);
}
