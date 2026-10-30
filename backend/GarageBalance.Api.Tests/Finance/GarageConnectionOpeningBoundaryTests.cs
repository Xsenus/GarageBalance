using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class GarageConnectionOpeningBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_ExplicitConnectionKeepsItsOpeningInCardHistoryAndGrid(bool closed)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertConnectionOpeningAsync(database.Context, closed);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ClosedConnectionKeepsItsOpeningInCardHistoryAndGrid()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertConnectionOpeningAsync(context, closed: true);
    }

    [Fact]
    public async Task ReplacementEdit_RejectsMovingBeforeTheExplicitConnectionWithoutChangingHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var (garage, _, service) = await SeedAsync(database.Context, closed: false);
        var replacement = await service.ReplaceMeterDeviceAsync(new(garage.Id, MeterKinds.Electricity,
            new(2026, 10, 1), new(2026, 10, 20), "CONNECTION-NEW", 0, 3, 120, "Замена"), null, default);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        var before = (await service.GetMeterDeviceReplacementAsync(replacement.Value!.Device.Id, default)).Value!;
        var result = await service.UpdateMeterDeviceReplacementAsync(before.Device.Id,
            new(before.Device.Version, before.RemovedDevice.Version, before.Reading!.Version, new(2026, 10, 1),
                new(2026, 10, 10), before.Device.SerialNumber, 120, 0, "Исправление"), null, default);
        Assert.False(result.Succeeded);
        Assert.Equal("meter_reading_outside_connection", result.ErrorCode);
        var after = (await service.GetMeterDeviceReplacementAsync(before.Device.Id, default)).Value!;
        Assert.Equal(before, after);
    }

    private static async Task AssertConnectionOpeningAsync(GarageBalanceDbContext context, bool closed)
    {
        var (garage, device, service) = await SeedAsync(context, closed);
        var starts = (await service.GetStartValuesAsync(garage, default)).Where(item => item.MeterKind == MeterKinds.Electricity).ToArray();
        Assert.NotEmpty(starts);
        Assert.All(starts, start => Assert.Equal(new DateOnly(2026, 10, 15), closed ? start.BaselineStartsOn : start.ConnectedOn));
        var history = await service.GetMeterDeviceHistoryAsync(garage.Id, MeterKinds.Electricity, 0, 25, default);
        Assert.Equal(new DateOnly(2026, 10, 15), Assert.Single(history.Items).AccountingOpeningDate);
        var grid = await new EfMeterReadingRepository(context).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, default);
        Assert.Equal(new DateOnly(2026, 10, 15), Assert.Single(grid.Garages).StartValueDate);
        Assert.Equal(new DateOnly(2026, 10, 15), (await context.MeterDevices.SingleAsync(item => item.Id == device.Id)).InstalledOn);
    }

    private static async Task<(Garage Garage, MeterDevice Device, FinanceService Service)> SeedAsync(GarageBalanceDbContext context, bool closed)
    {
        var garage = new Garage { Number = "CONNECTION-BOUNDARY", AccountingStartMonth = new(2026, 7, 1), RegisteredOn = new(2026, 7, 1), PeopleCount = 1, FloorCount = 1 };
        var income = new IncomeType { Name = "CONNECTION-BOUNDARY-SERVICE" };
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial, InstalledOn = new(2026, 10, 15), InitialValue = 100 };
        context.AddRange(garage, income, device,
            new ChargeServiceSetting { Name = "CONNECTION-BOUNDARY", IncomeType = income, IsRegular = true, IsMetered = true, MeterKind = MeterKinds.Electricity, PeriodicityMonths = 1, AccrualStartMonth = 1, PaymentDueDay = 20, UnitName = "кВтч" },
            new GarageMeterConnection { Garage = garage, MeterKind = MeterKinds.Electricity, ConnectedOn = new(2026, 10, 15), ClosedOn = closed ? new(2026, 10, 25) : null, BaselineMeterDevice = device });
        await context.SaveChangesAsync();
        return (garage, device, FinanceServiceTestFactory.Create(context, businessDate: new(2026, 10, 31)));
    }
}
