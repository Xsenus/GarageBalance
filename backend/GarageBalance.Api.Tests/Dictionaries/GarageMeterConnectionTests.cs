using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class GarageMeterConnectionTests
{
    private static readonly DateOnly ConnectedOn = new(2026, 9, 10);

    [Fact]
    public async Task NullStartValueItem_ReturnsValidationForBaselineAndGarageWrites()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, _) = await SeedAsync(context);
        var auditCount = await context.AuditEvents.CountAsync();
        var baseline = await FinanceServiceTestFactory.Create(context).ApplyStartValuesAsync(
            garage, [null!], null, CancellationToken.None);
        Assert.Equal("meter_kind_invalid", baseline.ErrorCode);
        var service = DictionaryServiceTestFactory.Create(context);
        var updated = await service.UpdateGarageAsync(garage.Id, Request(garage, null!), null, CancellationToken.None);
        Assert.Equal("meter_kind_invalid", updated.ErrorCode);
        var created = await service.CreateGarageAsync(Request(garage, null!) with { Number = "invalid-null-connection" }, null, CancellationToken.None);
        Assert.Equal("meter_kind_invalid", created.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Equal(1, await context.Garages.CountAsync());
        Assert.Empty(await context.GarageMeterConnections.ToArrayAsync());
        Assert.Empty(await context.MeterDevices.ToArrayAsync());
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task NewService_RequiresDateAndKeepsOtherHistoryUnchanged()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await SeedAsync(database.Context);
        var service = DictionaryServiceTestFactory.Create(database.Context);
        var result = await service.UpdateGarageAsync(garage.Id, Request(garage, new(setting.MeterKind!, 250m)), null, CancellationToken.None);

        Assert.Equal("meter_connection_date_required", result.ErrorCode);
        database.Context.ChangeTracker.Clear();
        Assert.Empty(await database.Context.GarageMeterConnections.ToListAsync());
        Assert.Empty(await database.Context.MeterDevices.ToListAsync());
        Assert.Equal(100m, (await database.Context.Garages.SingleAsync()).InitialWaterMeterValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(250)]
    public async Task NewService_StoresExplicitDateAndDistinguishesMissingFromZero(int? initialValue)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertNewConnectionAsync(database.Context, initialValue);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_NewConnectionPersistsWithoutRewritingLegacyHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertNewConnectionAsync(context, 250m);
    }

    private static async Task AssertNewConnectionAsync(GarageBalanceDbContext context, decimal? initialValue)
    {
        var (garage, setting) = await SeedAsync(context);
        var oldReading = new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new DateOnly(2026, 2, 1),
            ReadingDate = new DateOnly(2026, 2, 28),
            PreviousValue = 100m,
            CurrentValue = 123m,
            Consumption = 23m
        };
        context.MeterReadings.Add(oldReading);
        await context.SaveChangesAsync();
        var originalVersion = oldReading.Version;
        var service = DictionaryServiceTestFactory.Create(context);
        var result = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, initialValue, ConnectedOn)), null, CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var connection = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        Assert.Equal(ConnectedOn, connection.ConnectedOn);
        Assert.Equal(initialValue, connection.BaselineMeterDevice?.InitialValue);
        Assert.Equal(initialValue.HasValue, connection.BaselineMeterDeviceId.HasValue);
        Assert.Equal(initialValue.HasValue ? 1 : 0, await context.MeterDevices.CountAsync());
        var savedGarage = await context.Garages.SingleAsync();
        Assert.Equal(new DateOnly(2026, 1, 1), savedGarage.InitialMeterReadingMonth);
        Assert.Equal(100m, savedGarage.InitialWaterMeterValue);
        Assert.Equal(1000m, savedGarage.InitialElectricityMeterValue);
        var historical = await context.MeterReadings.SingleAsync();
        Assert.Equal((100m, 123m, 23m, originalVersion),
            (historical.PreviousValue, historical.CurrentValue, historical.Consumption, historical.Version));
        var values = await service.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None);
        var dto = Assert.Single(values.Value!, item => item.MeterKind == setting.MeterKind);
        Assert.Equal(ConnectedOn, dto.ConnectedOn);
        Assert.Equal(initialValue, dto.Value);
        Assert.Equal(!initialValue.HasValue, dto.RequiresStartValue);
        Assert.False(dto.RequiresConnectionDate);
        Assert.Equal(connection.Version, dto.ConnectionVersion);
        Assert.Single(await context.AuditEvents.Where(item => item.Action == "finance.meter_connection_changed").ToListAsync());
    }

    [Fact]
    public async Task WaitingConnection_CanReceiveZeroAndClearItBeforeReadings()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var service = DictionaryServiceTestFactory.Create(context);
        Assert.True((await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, null, ConnectedOn)), null, CancellationToken.None)).Succeeded);
        var connection = await context.GarageMeterConnections.SingleAsync();
        var filled = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 0m, ConnectedOn, connection.Version, "Получено показание")), null, CancellationToken.None);
        Assert.True(filled.Succeeded, filled.ErrorMessage);
        Assert.Equal(0m, (await context.MeterDevices.SingleAsync()).InitialValue);

        var cleared = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, null, ConnectedOn, connection.Version, "Исправление ошибочного нуля")), null, CancellationToken.None);
        Assert.True(cleared.Succeeded, cleared.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Null((await context.GarageMeterConnections.SingleAsync()).BaselineMeterDeviceId);
        Assert.True(Assert.Single((await service.GetGarageMeterStartValuesAsync(garage.Id, CancellationToken.None)).Value!).RequiresStartValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearedBase_DateCorrectionAndRefillKeepLogicalInstallationUsable(bool moveDateInSeparateSave)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var dictionary = DictionaryServiceTestFactory.Create(context);
        var created = await dictionary.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn)), null, CancellationToken.None);
        Assert.True(created.Succeeded, created.ErrorMessage);
        var connection = await context.GarageMeterConnections.SingleAsync();
        var originalDeviceId = connection.BaselineMeterDeviceId;
        var cleared = await dictionary.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, null, ConnectedOn, connection.Version, "Стартовое показание требует уточнения")), null, CancellationToken.None);
        Assert.True(cleared.Succeeded, cleared.ErrorMessage);
        var earlierDate = ConnectedOn.AddDays(-5);
        if (moveDateInSeparateSave)
        {
            var dated = await dictionary.UpdateGarageAsync(garage.Id,
                Request(garage, new(setting.MeterKind!, null, earlierDate, connection.Version, "Уточнена дата подключения")), null, CancellationToken.None);
            Assert.True(dated.Succeeded, dated.ErrorMessage);
        }
        var refilled = await dictionary.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 240m, earlierDate, connection.Version, "Получен акт подключения")), null, CancellationToken.None);
        Assert.True(refilled.Succeeded, refilled.ErrorMessage);
        context.ChangeTracker.Clear();
        var stored = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        Assert.Equal(originalDeviceId, stored.BaselineMeterDeviceId);
        Assert.Equal(earlierDate, stored.ConnectedOn);
        Assert.Equal(earlierDate, stored.BaselineMeterDevice!.InstalledOn);
        Assert.Equal(240m, stored.BaselineMeterDevice.InitialValue);
        Assert.Single(await context.MeterDevices.ToListAsync());
        var reading = await FinanceServiceTestFactory.Create(context).CreateMeterReadingAsync(
            new(garage.Id, setting.MeterKind!, new DateOnly(2026, 9, 1), earlierDate.AddDays(1), 241m, null), null, CancellationToken.None);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(1m, reading.Value!.Consumption);
    }

    [Fact]
    public async Task ExistingConnection_RequiresVersionAndCorrectionReason()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var service = DictionaryServiceTestFactory.Create(context);
        Assert.True((await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn)), null, CancellationToken.None)).Succeeded);
        var connection = await context.GarageMeterConnections.SingleAsync();

        var missingVersion = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 251m, ConnectedOn, Reason: "Исправление")), null, CancellationToken.None);
        Assert.Equal("meter_connection_version_required", missingVersion.ErrorCode);
        var missingReason = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 251m, ConnectedOn, connection.Version)), null, CancellationToken.None);
        Assert.Equal("meter_connection_reason_required", missingReason.ErrorCode);
        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() => service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 251m, ConnectedOn, Guid.NewGuid(), "Исправление")), null, CancellationToken.None));
        context.ChangeTracker.Clear();
        Assert.Equal(250m, (await context.MeterDevices.SingleAsync()).InitialValue);
    }

    [Fact]
    public async Task ExistingConnection_UnchangedSaveDoesNotCreateAuditOrRotateVersion()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var service = DictionaryServiceTestFactory.Create(context);
        Assert.True((await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn)), null, CancellationToken.None)).Succeeded);
        var version = (await context.GarageMeterConnections.SingleAsync()).Version;
        var auditCount = await context.AuditEvents.CountAsync();
        var result = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn, version)), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(version, (await context.GarageMeterConnections.SingleAsync()).Version);
        Assert.Equal(auditCount, await context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task ExistingConnection_CorrectsItsChainAndAuditsDateAndReason()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var service = DictionaryServiceTestFactory.Create(context);
        Assert.True((await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn)), null, CancellationToken.None)).Succeeded);
        var connection = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            MeterDevice = connection.BaselineMeterDevice,
            AccountingMonth = new DateOnly(2026, 9, 1),
            ReadingDate = new DateOnly(2026, 9, 30),
            PreviousValue = 250m,
            CurrentValue = 273m,
            Consumption = 23m
        });
        await context.SaveChangesAsync();
        var before = connection.Version;
        var corrected = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 240m, ConnectedOn.AddDays(-2), connection.Version, "Акт подключения уточнён")), null, CancellationToken.None);
        Assert.True(corrected.Succeeded, corrected.ErrorMessage);
        context.ChangeTracker.Clear();
        var saved = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        Assert.Equal(ConnectedOn.AddDays(-2), saved.ConnectedOn);
        Assert.Equal(saved.ConnectedOn, saved.BaselineMeterDevice!.InstalledOn);
        Assert.NotEqual(before, saved.Version);
        Assert.Equal(240m, saved.BaselineMeterDevice.InitialValue);
        var reading = await context.MeterReadings.SingleAsync();
        Assert.Equal((240m, 33m), (reading.PreviousValue, reading.Consumption));
        var audits = await context.AuditEvents.Where(item => item.Action == "finance.meter_connection_changed").ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, audit => System.Text.Json.JsonDocument.Parse(audit.MetadataJson!)
            .RootElement.TryGetProperty("reason", out var reason) && reason.GetString() == "Акт подключения уточнён");
    }

    [Fact]
    public async Task ExistingReadings_PreventClearingTheConnectionStartValue()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var service = DictionaryServiceTestFactory.Create(context);
        Assert.True((await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn)), null, CancellationToken.None)).Succeeded);
        var connection = await context.GarageMeterConnections.Include(item => item.BaselineMeterDevice).SingleAsync();
        context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            MeterDevice = connection.BaselineMeterDevice,
            AccountingMonth = new DateOnly(2026, 9, 1),
            ReadingDate = new DateOnly(2026, 9, 30),
            PreviousValue = 250m,
            CurrentValue = 273m,
            Consumption = 23m
        });
        await context.SaveChangesAsync();
        var result = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, null, ConnectedOn, connection.Version, "Исправление")), null, CancellationToken.None);
        Assert.Equal("meter_connection_start_value_required", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.NotNull((await context.GarageMeterConnections.SingleAsync()).BaselineMeterDeviceId);
    }

    [Theory]
    [InlineData(2026, 10, 1)]
    [InlineData(2026, 9, 30)]
    public async Task ExistingReadings_PreventConnectionDateThatCutsHistory(int year, int month, int day)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            AccountingMonth = new DateOnly(2026, 9, 1),
            ReadingDate = new DateOnly(2026, 9, 29),
            PreviousValue = 250m,
            CurrentValue = 273m,
            Consumption = 23m
        });
        await context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, new DateOnly(year, month, day), Reason: "Уточнение даты")), null, CancellationToken.None);
        Assert.Equal("meter_connection_date_after_history", result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GarageMeterConnections.ToListAsync());
        Assert.Equal(23m, (await context.MeterReadings.SingleAsync()).Consumption);
    }

    [Fact]
    public async Task LegacyStartValue_RequiresReasonToBecomeExplicitAndPreservesOpeningColumns()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        setting.MeterKind = MeterKinds.Water;
        await context.SaveChangesAsync();
        var service = DictionaryServiceTestFactory.Create(context);
        var missingReason = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 110m, ConnectedOn)), null, CancellationToken.None);
        Assert.Equal("meter_connection_reason_required", missingReason.ErrorCode);
        var saved = await service.UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 110m, ConnectedOn, Reason: "Уточнение подключения")), null, CancellationToken.None);
        Assert.True(saved.Succeeded, saved.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Equal(100m, (await context.Garages.SingleAsync()).InitialWaterMeterValue);
        Assert.Equal(110m, (await context.MeterDevices.SingleAsync()).InitialValue);
        Assert.Equal(ConnectedOn, (await context.GarageMeterConnections.SingleAsync()).ConnectedOn);
    }

    [Fact]
    public async Task NewGarage_AcceptsIndependentConnectionDatesAndWaitingServices()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var water = new ChargeServiceSetting { Name = "Вода", IsRegular = true, IsMetered = true, MeterKind = MeterKinds.Water };
        var electricity = new ChargeServiceSetting { Name = "Электричество", IsRegular = true, IsMetered = true, MeterKind = MeterKinds.Electricity };
        context.ChargeServiceSettings.AddRange(water, electricity);
        await context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(context).CreateGarageAsync(
            new UpsertGarageRequest("new", 1, 1, null, 0m, null, null, null, MeterStartValues:
                [new(MeterKinds.Water, 0m, ConnectedOn), new(MeterKinds.Electricity, null, ConnectedOn.AddMonths(1))]), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.GarageMeterConnections.CountAsync());
        Assert.Single(await context.MeterDevices.ToListAsync());
        var garage = await context.Garages.SingleAsync();
        Assert.Null(garage.InitialWaterMeterValue);
        Assert.Null(garage.InitialElectricityMeterValue);
    }

    [Fact]
    public async Task LegacyReading_UsesPendingConnectionDateWhenRebuildingGapWarning()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            AccountingMonth = new DateOnly(2026, 9, 1),
            ReadingDate = new DateOnly(2026, 9, 30),
            PreviousValue = 250m,
            CurrentValue = 273m,
            Consumption = 23m,
            HasGapWarning = true
        });
        await context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(setting.MeterKind!, 250m, ConnectedOn, Reason: "Уточнена дата начала услуги")), null, CancellationToken.None);
        Assert.True(result.Succeeded, result.ErrorMessage);
        context.ChangeTracker.Clear();
        var reading = await context.MeterReadings.SingleAsync();
        Assert.Equal((250m, 23m), (reading.PreviousValue, reading.Consumption));
        Assert.False(reading.HasGapWarning);
        Assert.NotNull(reading.MeterDeviceId);
    }

    [Fact]
    public async Task PendingConnectionQuery_ObservesCancellation()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, setting) = await SeedAsync(database.Context);
        database.Context.GarageMeterConnections.Add(new GarageMeterConnection
        {
            Garage = garage,
            MeterKind = setting.MeterKind!,
            ConnectedOn = ConnectedOn
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EfMeterReadingRepository(database.Context)
            .GetConnectionForUpdateAsync(garage.Id, setting.MeterKind!, cancellation.Token));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ConnectionUpdateRejectsConcurrentChangesAndDuplicateActivePeriods()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var first = database.CreateContext();
        var (garage, setting) = await SeedAsync(first);
        var connection = new GarageMeterConnection { Garage = garage, MeterKind = setting.MeterKind!, ConnectedOn = ConnectedOn };
        first.GarageMeterConnections.Add(connection);
        await first.SaveChangesAsync();
        await using var second = database.CreateContext();
        var stale = await second.GarageMeterConnections.SingleAsync();
        connection.ConnectedOn = ConnectedOn.AddDays(1);
        await new EfApplicationUnitOfWork(first).SaveChangesAsync(CancellationToken.None);
        stale.ConnectedOn = ConnectedOn.AddDays(2);
        await Assert.ThrowsAsync<ApplicationConcurrencyException>(() => new EfApplicationUnitOfWork(second).SaveChangesAsync(CancellationToken.None));
        second.ChangeTracker.Clear();
        Assert.Equal(ConnectedOn.AddDays(1), (await second.GarageMeterConnections.SingleAsync()).ConnectedOn);
        second.GarageMeterConnections.Add(new GarageMeterConnection
        {
            GarageId = garage.Id,
            MeterKind = setting.MeterKind!,
            ConnectedOn = ConnectedOn.AddMonths(1)
        });
        await Assert.ThrowsAsync<ApplicationPersistenceConflictException>(() => new EfApplicationUnitOfWork(second).SaveChangesAsync(CancellationToken.None));
        second.ChangeTracker.Clear();
        Assert.Single(await second.GarageMeterConnections.ToListAsync());
    }

    [Theory]
    [InlineData("date")]
    [InlineData("reason")]
    [InlineData("stale-new")]
    public async Task NewConnection_RejectsInvalidDateReasonAndUnexpectedVersion(string invalidField)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        var request = new UpsertGarageMeterStartValueRequest(setting.MeterKind!, 0m,
            invalidField == "date" ? new DateOnly(1899, 12, 31) : ConnectedOn,
            invalidField == "stale-new" ? Guid.NewGuid() : null,
            invalidField == "reason" ? new string('a', 1001) : null);
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, request), null, CancellationToken.None);
        Assert.Equal(invalidField switch
        {
            "date" => "meter_connection_date_invalid",
            "reason" => "meter_connection_reason_invalid",
            _ => "meter_connection_changed"
        }, result.ErrorCode);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GarageMeterConnections.ToListAsync());
    }

    [Fact]
    public async Task RestrictedLegacyKind_CannotBeConnectedOutsideItsGarages()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, setting) = await SeedAsync(context);
        setting.MeterKind = MeterKinds.Water;
        setting.AppliesToSelectedGarages = true;
        setting.GarageIds = [Guid.NewGuid()];
        await context.SaveChangesAsync();
        var result = await DictionaryServiceTestFactory.Create(context).UpdateGarageAsync(garage.Id,
            Request(garage, new(MeterKinds.Water, 0m, ConnectedOn, Reason: "Подключение")), null, CancellationToken.None);
        Assert.Equal("meter_start_service_not_applicable", result.ErrorCode);
        Assert.Empty(await context.GarageMeterConnections.ToListAsync());
    }

    private static async Task<(Garage Garage, ChargeServiceSetting Setting)> SeedAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage
        {
            Number = "connection-synthetic",
            InitialWaterMeterValue = 100m,
            InitialElectricityMeterValue = 1000m,
            InitialMeterReadingMonth = new DateOnly(2026, 1, 1)
        };
        var setting = new ChargeServiceSetting
        {
            Name = "Дополнительный счётчик",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.ForService(Guid.NewGuid()),
            UnitName = "кВт·ч"
        };
        context.Garages.Add(garage);
        context.ChargeServiceSettings.Add(setting);
        await context.SaveChangesAsync();
        return (garage, setting);
    }

    private static UpsertGarageRequest Request(Garage garage, UpsertGarageMeterStartValueRequest start) =>
        new(garage.Number, garage.PeopleCount, garage.FloorCount, garage.OwnerId, garage.StartingBalance,
            garage.InitialWaterMeterValue, garage.InitialElectricityMeterValue, garage.Comment,
            garage.Version, garage.StartingOverdueDebt, [start]);
}
