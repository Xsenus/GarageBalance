using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed partial class FinanceServiceTests
{
    [Theory]
    [InlineData(7, 10, "Начало учёта 01.10.2026")]
    [InlineData(8, 8, "Начало учёта 01.08.2026")]
    public async Task CustomerOpening_UsesAccountingMonthForCardAndFirstConsumption(int month, int installedMonth, string serial)
    {
        await using var database = await TestDatabase.CreateAsync();
        var fixtures = await database.SeedAsync();
        fixtures.Garage.AccountingStartMonth = new(2026, month, 1);
        fixtures.Garage.InitialMeterReadingMonth = new(2026, 9, 1);
        database.Context.ChargeServiceSettings.Add(new ChargeServiceSetting
        {
            Name = "Электроэнергия",
            IsRegular = true,
            IsMetered = true,
            MeterKind = MeterKinds.Electricity,
            IncomeType = fixtures.IncomeType,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 20,
            UnitName = "кВтч"
        });
        var device = new MeterDevice
        {
            GarageId = fixtures.Garage.Id,
            MeterKind = MeterKinds.Electricity,
            SerialNumber = serial,
            InitialValue = 100m,
            InstalledOn = new(2026, installedMonth, 1)
        };
        database.Context.MeterDevices.Add(device);
        var september = new MeterReading
        {
            GarageId = fixtures.Garage.Id,
            MeterKind = MeterKinds.Electricity,
            MeterDevice = device,
            AccountingMonth = new(2026, 9, 1),
            ReadingDate = new(2026, 9, 20),
            CurrentValue = 180,
            Consumption = 80
        };
        database.Context.MeterReadings.Add(september);
        await database.Context.SaveChangesAsync();
        var service = FinanceServiceTestFactory.Create(database.Context);

        var baseline = (await service.GetStartValuesAsync(fixtures.Garage, default)).Single(item => item.MeterKind == MeterKinds.Electricity);
        Assert.Equal(new DateOnly(2026, month, 1), baseline.BaselineStartsOn);
        Assert.Equal(100m, baseline.Value);
        var reading = await service.CreateMeterReadingAsync(new(fixtures.Garage.Id, MeterKinds.Electricity,
            new(2026, month, 1), new(2026, month, 20), 150m, null), null, default);
        Assert.True(reading.Succeeded, reading.ErrorMessage);
        Assert.Equal(50m, reading.Value!.Consumption);
        Assert.Equal(device.Id, reading.Value.MeterDeviceId);
        Assert.Equal(new DateOnly(2026, installedMonth, 1), device.InstalledOn);
        Assert.Equal(30m, (await database.Context.MeterReadings.SingleAsync(item => item.Id == september.Id)).Consumption);
    }
}
