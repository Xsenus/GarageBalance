using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class StoredMeterKindsTests
{
    [Fact]
    public async Task StoredKinds_AreDistinctIncludeAllHistoryAndExcludeOtherGarages()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "meter-kind-test" };
        var other = new Garage { Number = "other-kind-test" };
        context.AddRange(garage, other);
        var repository = new EfMeterReadingRepository(context);
        Assert.Empty(await repository.GetStoredMeterKindsAsync(garage.Id, CancellationToken.None));
        var custom = MeterKinds.ForService(Guid.NewGuid());
        var device = new MeterDevice { Garage = garage, MeterKind = custom, SerialNumber = "synthetic-custom", InstalledOn = new(2026, 9, 1) };
        context.AddRange(device,
            new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "synthetic-canceled", InstalledOn = new(2026, 9, 1), IsCanceled = true },
            new MeterDevice { Garage = other, MeterKind = MeterKinds.ForService(Guid.NewGuid()), SerialNumber = "synthetic-other", InstalledOn = new(2026, 9, 1) },
            new GarageMeterConnection { Garage = garage, MeterKind = custom, BaselineMeterDevice = device, ConnectedOn = new(2026, 9, 1) });
        foreach (var month in new[] { 9, 10 }) context.MeterReadings.Add(new MeterReading
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            AccountingMonth = new(2026, month, 1),
            ReadingDate = new(2026, month, 20),
            CurrentValue = month,
            Consumption = month,
            IsCanceled = month == 9
        });
        await context.SaveChangesAsync();
        var kinds = await repository.GetStoredMeterKindsAsync(garage.Id, CancellationToken.None);
        Assert.Equal(3, kinds.Count);
        Assert.Contains(custom, kinds);
        Assert.Contains(MeterKinds.Water, kinds);
        Assert.Contains(MeterKinds.Electricity, kinds);
        Assert.Equal(kinds.Order(StringComparer.Ordinal), kinds);
    }

    [Fact]
    public async Task StoredKinds_AreBoundedAndOversizedCorrectionDoesNotChangeData()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = new Garage { Number = "many-kinds", RegisteredOn = new(2026, 10, 1) };
        context.Garages.Add(garage);
        context.MeterDevices.AddRange(Enumerable.Range(1, 502).Select(index => new MeterDevice
        {
            Garage = garage,
            MeterKind = $"service_{index:x32}",
            SerialNumber = $"synthetic-{index}",
            InstalledOn = new(2026, 9, 1)
        }));
        await context.SaveChangesAsync();
        Assert.Equal(501, (await new EfMeterReadingRepository(context).GetStoredMeterKindsAsync(garage.Id, CancellationToken.None)).Count);
        var result = await FinanceServiceTestFactory.Create(context, businessDate: new(2026, 10, 1))
            .BeginAsync(garage, new(2026, 9, 1), garage.Version, false, null, CancellationToken.None);
        Assert.Equal("garage_accounting_start_meter_limit", result.ErrorCode);
        Assert.Null((await context.Garages.SingleAsync()).AccountingStartMonth);
        Assert.Equal(502, await context.MeterDevices.CountAsync());
    }
}
