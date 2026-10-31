using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class CustomerRestartBaselineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_RestartResolvesAccountingChainWithoutRewritingEarlierDevices(bool hasEarlierReadings)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await VerifyAsync(database.Context, hasEarlierReadings);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RestartResolvesAccountingChainWithoutRewritingEarlierDevices()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await VerifyAsync(context, false);
        await VerifyAsync(context, true);
    }

    private static async Task VerifyAsync(GarageBalanceDbContext context, bool hasEarlierReadings)
    {
        foreach (var setting in await context.ChargeServiceSettings.ToArrayAsync()) setting.IsArchived = true;
        var garage = new Garage
        {
            Number = $"SYNTHETIC-RESTART-{hasEarlierReadings}",
            AccountingStartMonth = new(2026, hasEarlierReadings ? 8 : 7, 1),
            InitialMeterReadingMonth = new(2026, hasEarlierReadings ? 7 : 6, 1),
            InitialElectricityMeterValue = 100,
            RegisteredOn = new(2026, 7, 1)
        };
        var income = new IncomeType { Name = garage.Number };
        var old = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial,
            InitialValue = 0,
            InstalledOn = new(2026, 8, 1),
            RemovedOn = new(2026, 9, 30),
            FinalValue = 90
        };
        var opening = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = "Начало учёта 01.10.2026",
            InitialValue = 100,
            InstalledOn = new(2026, 10, 1)
        };
        context.AddRange(garage, income, old, opening,
            new ChargeServiceSetting
            {
                Name = garage.Number,
                IncomeType = income,
                IsRegular = true,
                IsMetered = true,
                MeterKind = MeterKinds.Electricity,
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20,
                UnitName = "кВтч"
            });
        if (hasEarlierReadings)
            context.Add(new MeterReading
            {
                Garage = garage,
                MeterKind = MeterKinds.Electricity,
                MeterDevice = old,
                AccountingMonth = new(2026, 8, 1),
                ReadingDate = new(2026, 8, 20),
                CurrentValue = 80,
                Consumption = 80
            });
        await context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(context, businessDate: new(2026, 10, 31));
        var baseline = (await service.GetStartValuesAsync(garage, default)).First(item => item.MeterKind == MeterKinds.Electricity);
        Assert.Equal(new DateOnly(2026, hasEarlierReadings ? 8 : 7, 1), baseline.BaselineStartsOn);
        Assert.Equal(hasEarlierReadings ? 0m : 100m, baseline.Value);
        var grid = await new EfMeterReadingRepository(context).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, default);
        var row = grid.Garages.Single(item => item.Id == garage.Id);
        Assert.Equal(baseline.BaselineStartsOn, row.StartValueDate);
        Assert.Equal(baseline.Value, row.StartValue);
        var history = await service.GetMeterDeviceHistoryAsync(garage.Id, MeterKinds.Electricity, 0, 25, default);
        Assert.Equal(hasEarlierReadings ? null : new DateOnly(2026, 7, 1), history.Items.Single(item => item.Id == opening.Id).AccountingOpeningDate);
        Assert.Equal(hasEarlierReadings ? new DateOnly(2026, 8, 1) : null, history.Items.Single(item => item.Id == old.Id).AccountingOpeningDate);
        var july = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity,
            new(2026, 7, 1), new(2026, 7, 20), 150, null), null, default);
        if (hasEarlierReadings)
        {
            Assert.Equal("meter_reading_before_accounting_start", july.ErrorCode);
            var october = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity,
                new(2026, 10, 1), new(2026, 10, 20), 150, null), null, default);
            Assert.True(october.Succeeded, october.ErrorMessage);
            Assert.Equal((opening.Id, 50m), (october.Value!.MeterDeviceId, october.Value.Consumption));
            var year = await service.GetMeterReadingYearPageAsync(new(2026, MeterKinds.Electricity, 25, 0), default);
            Assert.True(year.Succeeded, year.ErrorMessage);
            var marker = year.Value!.Readings.Single(item => item.Id == october.Value.Id).MeterReplacement;
            Assert.NotNull(marker);
            Assert.True(marker.IsAccountingRestart);
            var worksheet = await service.GetGarageIncomeWorksheetAsync(garage.Id, new(new(2026, 10, 1), new(2026, 10, 1)), default);
            Assert.True(worksheet.Succeeded, worksheet.ErrorMessage);
            Assert.True(worksheet.Value!.Rows.Single(row => row.IncomeTypeId == income.Id).MeterReplacement!.IsAccountingRestart);
            Assert.Equal((90m, 100m, 150m, 0m, 50m), (marker.OldFinalValue, marker.NewInitialValue, marker.CurrentValue, marker.RemovedDevicesConsumption, marker.TotalConsumption));
            var november = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity,
                new(2026, 11, 1), new(2026, 11, 20), 170, null), null, default);
            Assert.True(november.Succeeded, november.ErrorMessage);
            var markers = await new EfMeterReadingRepository(context).GetReplacementMarkersAsync([october.Value.Id, november.Value!.Id], default);
            Assert.Equal(october.Value.Id, Assert.Single(markers).Key);
        }
        else
        {
            Assert.True(july.Succeeded, july.ErrorMessage);
            Assert.Equal((opening.Id, 50m), (july.Value!.MeterDeviceId, july.Value.Consumption));
            var august = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity,
                new(2026, 8, 1), new(2026, 8, 20), 170, null), null, default);
            Assert.True(august.Succeeded, august.ErrorMessage);
            Assert.Equal((opening.Id, 20m), (august.Value!.MeterDeviceId, august.Value.Consumption));
        }
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30), 90m), (old.InstalledOn, old.RemovedOn, old.FinalValue));
        Assert.Equal(new DateOnly(2026, 10, 1), opening.InstalledOn);
        Assert.Equal(2, await context.MeterDevices.CountAsync(item => item.GarageId == garage.Id));
    }
}
