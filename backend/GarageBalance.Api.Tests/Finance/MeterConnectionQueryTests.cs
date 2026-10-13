using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;

namespace GarageBalance.Api.Tests.Finance;

public sealed class MeterConnectionQueryTests
{
    private static readonly string[] Kinds = [MeterKinds.Water, MeterKinds.Electricity, "service_11111111111111111111111111111111"];

    [Theory]
    [InlineData(MeterKinds.Water)]
    [InlineData(MeterKinds.Electricity)]
    [InlineData("service_11111111111111111111111111111111")]
    public async Task SqliteQueriesKeepHistoryAndApplyDatedReadinessBeforePagination(string kind)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyQueriesAsync(database.Context, kind);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlQueriesKeepHistoryAndApplyDatedReadinessBeforePagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        foreach (var kind in Kinds)
        {
            await VerifyQueriesAsync(context, kind);
        }
    }

    [Fact]
    public async Task SqliteMixedKindsDoNotTreatAnUnpreparedMeterAsMissing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyMixedKindsAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlMixedKindsDoNotTreatAnUnpreparedMeterAsMissing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyMixedKindsAsync(context);
    }

    private static async Task VerifyMixedKindsAsync(GarageBalanceDbContext context)
    {
        var month = new DateOnly(2045, 9, 1);
        var garage = NewGarage("MIXED");
        garage.InitialWaterMeterValue = 0;
        garage.InitialMeterReadingMonth = month;
        context.Garages.Add(garage);
        AddConnection(context, garage, MeterKinds.Electricity, month.AddDays(9), hasBase: false);
        AddConnection(context, garage, Kinds[2], month.AddMonths(1));
        await context.SaveChangesAsync();
        var query = new EfMissingMeterReadingQuery(context);
        Assert.Empty(await query.GetMissingAsync(month, Kinds, null, 20, CancellationToken.None));

        garage.InitialMeterReadingMonth = month.AddMonths(-1);
        await context.SaveChangesAsync();
        var waterOnly = await query.GetMissingAsync(month, Kinds, null, 20, CancellationToken.None);
        Assert.Equal(MeterKinds.Water, Assert.Single(waterOnly).MeterKind);
        var limited = await query.GetMissingAsync(month, Kinds, null, 1, CancellationToken.None);
        Assert.Equal(MeterKinds.Water, Assert.Single(limited).MeterKind);

        var october = await query.GetMissingAsync(month.AddMonths(1), Kinds, null, 20, CancellationToken.None);
        Assert.Equal(new[] { MeterKinds.Water, Kinds[2] }, october.Select(row => row.MeterKind));
        var lastSupportedMonth = await query.GetMissingAsync(new DateOnly(9999, 12, 1), Kinds, null, 20, CancellationToken.None);
        Assert.Equal(new[] { MeterKinds.Water, Kinds[2] }, lastSupportedMonth.Select(row => row.MeterKind));
    }

    private static async Task VerifyQueriesAsync(GarageBalanceDbContext context, string kind)
    {
        context.GarageMeterConnections.RemoveRange(context.GarageMeterConnections);
        context.MeterReadings.RemoveRange(context.MeterReadings);
        await context.SaveChangesAsync();
        context.MeterDevices.RemoveRange(context.MeterDevices);
        context.Garages.RemoveRange(context.Garages);
        context.ChargeServiceSettings.RemoveRange(context.ChargeServiceSettings);
        await context.SaveChangesAsync();

        var september = new DateOnly(2045, 9, 1);
        var future = NewGarage("1");
        var closedBeforeYear = NewGarage("2");
        var ready = NewGarage("3");
        var missingBase = NewGarage("4");
        var missingDate = NewGarage("5");
        var outsideCurrentScope = NewGarage("6");
        var historicalBeforeConnection = NewGarage("7");
        var legacyDevice = NewGarage("8");
        var closedDuringYear = NewGarage("9");
        var legacyReading = NewGarage("10");
        var legacyCanceledOnly = NewGarage("11");
        var deviceNotYetInstalled = NewGarage("12");
        var garages = new[] { future, closedBeforeYear, ready, missingBase, missingDate, outsideCurrentScope,
            historicalBeforeConnection, legacyDevice, closedDuringYear, legacyReading, legacyCanceledOnly, deviceNotYetInstalled };
        context.Garages.AddRange(garages);
        context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Подключаемая услуга",
            IsRegular = true,
            IsMetered = true,
            MeterKind = kind,
            AppliesToSelectedGarages = true,
            GarageIds = garages.Where(garage => garage != outsideCurrentScope).Select(garage => garage.Id).ToArray()
        });
        AddConnection(context, future, kind, new DateOnly(2046, 1, 1));
        AddConnection(context, closedBeforeYear, kind, new DateOnly(2044, 1, 1), new DateOnly(2044, 12, 31));
        AddConnection(context, ready, kind, new DateOnly(2045, 9, 10));
        AddConnection(context, missingBase, kind, new DateOnly(2045, 9, 10), hasBase: false);
        AddConnection(context, historicalBeforeConnection, kind, new DateOnly(2046, 1, 1));
        AddConnection(context, closedDuringYear, kind, new DateOnly(2045, 1, 1), new DateOnly(2045, 8, 31));
        context.MeterDevices.Add(NewDevice(legacyDevice, kind, new DateOnly(2045, 8, 1)));
        context.MeterDevices.Add(NewDevice(deviceNotYetInstalled, kind, new DateOnly(2045, 10, 1)));
        context.MeterReadings.AddRange(
            NewReading(outsideCurrentScope, kind, new DateOnly(2045, 1, 1)),
            NewReading(historicalBeforeConnection, kind, new DateOnly(2045, 1, 1)),
            NewReading(legacyReading, kind, new DateOnly(2045, 8, 1)),
            NewReading(legacyCanceledOnly, kind, new DateOnly(2045, 8, 1), canceled: true));
        await context.SaveChangesAsync();

        var repository = new EfMeterReadingRepository(context);
        var query = new EfMissingMeterReadingQuery(context);
        var first = await repository.GetYearPageAsync(2045, kind, 0, 1, CancellationToken.None);
        Assert.Equal(10, first.TotalCount);
        var readyRow = Assert.Single(first.Garages);
        Assert.Equal(ready.Id, readyRow.Id);
        Assert.Equal(new DateOnly(2045, 9, 10), readyRow.ConnectedOn);
        Assert.False(readyRow.RequiresStartValue);
        Assert.False(readyRow.RequiresConnectionDate);
        Assert.Null(readyRow.InitialReadingMonth);

        var page = await repository.GetYearPageAsync(2045, kind, 0, 20, CancellationToken.None);
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(3, page.Readings.Count);
        Assert.Contains(page.Readings, reading => reading.GarageId == outsideCurrentScope.Id);
        Assert.Contains(page.Readings, reading => reading.GarageId == historicalBeforeConnection.Id);
        var baseRow = Assert.Single(page.Garages, garage => garage.Id == missingBase.Id);
        Assert.True(baseRow.RequiresStartValue);
        Assert.False(baseRow.RequiresConnectionDate);
        var dateRow = Assert.Single(page.Garages, garage => garage.Id == missingDate.Id);
        Assert.True(dateRow.RequiresStartValue);
        Assert.True(dateRow.RequiresConnectionDate);
        Assert.Null(dateRow.ConnectedOn);
        Assert.True(Assert.Single(page.Garages, garage => garage.Id == legacyCanceledOnly.Id).RequiresStartValue);
        Assert.False(Assert.Single(page.Garages, garage => garage.Id == legacyDevice.Id).RequiresStartValue);
        Assert.False(Assert.Single(page.Garages, garage => garage.Id == legacyReading.Id).RequiresStartValue);
        Assert.Equal(new DateOnly(2045, 8, 31), Assert.Single(page.Garages, garage => garage.Id == closedDuringYear.Id).ClosedOn);

        var beyondEnd = await repository.GetYearPageAsync(2045, kind, 20, 1, CancellationToken.None);
        Assert.Equal(10, beyondEnd.TotalCount);
        Assert.Empty(beyondEnd.Garages);
        Assert.Empty(beyondEnd.Readings);

        var missing = await query.GetMissingAsync(september, [kind], null, 20, CancellationToken.None);
        Assert.Equal(new[] { ready.Id, legacyDevice.Id, legacyReading.Id }.Order(), missing.Select(row => row.GarageId).Order());
        Assert.Equal(legacyReading.Id, Assert.Single(await query.GetMissingAsync(september, [kind], null, 1, CancellationToken.None)).GarageId);
        Assert.Equal(ready.Id, Assert.Single(await query.GetMissingAsync(september, [kind], "3", 1, CancellationToken.None)).GarageId);
        Assert.Empty(await query.GetMissingAsync(september, [kind], "4", 20, CancellationToken.None));
        var beforeConnection = await query.GetMissingAsync(september.AddMonths(-1), [kind], null, 20, CancellationToken.None);
        Assert.DoesNotContain(beforeConnection, row => row.GarageId == ready.Id || row.GarageId == missingBase.Id);
        Assert.Contains(beforeConnection, row => row.GarageId == closedDuringYear.Id);
        Assert.Empty(await query.GetMissingAsync(september, [], null, 20, CancellationToken.None));
        Assert.Empty(await query.GetMissingAsync(september, [kind], null, 0, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.GetMissingAsync(september, [kind], null, 20, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetYearPageAsync(2045, kind, 0, 20, cancellation.Token));
    }

    private static Garage NewGarage(string number) => new() { Number = number, PeopleCount = 1, FloorCount = 1 };

    private static MeterDevice NewDevice(Garage garage, string kind, DateOnly installedOn) => new()
    {
        Garage = garage,
        MeterKind = kind,
        SerialNumber = "CONNECTION-BASE",
        InstalledOn = installedOn,
        InitialValue = 250
    };

    private static void AddConnection(GarageBalanceDbContext context, Garage garage, string kind, DateOnly connectedOn,
        DateOnly? closedOn = null, bool hasBase = true)
    {
        var device = hasBase ? NewDevice(garage, kind, connectedOn) : null;
        context.GarageMeterConnections.Add(new GarageMeterConnection
        {
            Garage = garage,
            MeterKind = kind,
            ConnectedOn = connectedOn,
            ClosedOn = closedOn,
            BaselineMeterDevice = device,
            BaselineMeterDeviceId = device?.Id
        });
    }

    private static MeterReading NewReading(Garage garage, string kind, DateOnly month, bool canceled = false) => new()
    {
        Garage = garage,
        MeterKind = kind,
        AccountingMonth = month,
        ReadingDate = month.AddDays(10),
        PreviousValue = 250,
        CurrentValue = 260,
        Consumption = 10,
        IsCanceled = canceled
    };
}
