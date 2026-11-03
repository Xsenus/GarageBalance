using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PreservedMeterBaselineTests
{
    [Fact]
    public async Task RemovingAllSixReadings_PreservesCardGridHistoryAndLaterConsumption()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        foreach (var group in await database.Context.MeterDevices.ToListAsync())
            group.IsAccountingBaseline = group.Id == seed.Original.Id || group.Id == seed.OtherDevice.Id;
        await database.Context.SaveChangesAsync();
        Assert.Equal(6, await database.Context.MeterReadings.CountAsync());
        await database.Context.MeterReadings.ExecuteDeleteAsync();
        database.Context.ChangeTracker.Clear();
        await VerifyAfterCleanupAsync(database.Context, seed);
    }

    internal static async Task VerifyAfterCleanupAsync(GarageBalanceDbContext db, Seed seed)
    {
        var service = FinanceServiceTestFactory.Create(db, businessDate: new(2026, 11, 20));
        var garage = await db.Garages.SingleAsync(item => item.Id == seed.Garage.Id);
        var card = (await service.GetStartValuesAsync(garage, default)).Single(item => item.MeterKind == MeterKinds.Electricity);
        Assert.Equal((980m, new DateOnly(2026, 8, 1), false), (card.Value, card.BaselineStartsOn, card.HasReadings));
        var grid = await new EfMeterReadingRepository(db).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, default);
        var row = grid.Garages.Single(item => item.Id == garage.Id);
        Assert.Equal((980m, new DateOnly(2026, 8, 1)), (row.StartValue, row.StartValueDate));
        Assert.Empty(grid.Readings);
        var history = await service.GetMeterDeviceHistoryAsync(garage.Id, MeterKinds.Electricity, 0, 25, default);
        Assert.Equal(new DateOnly(2026, 10, 1), history.Items.Single(item => item.Id == seed.Current.Id).InstalledOn);
        Assert.Empty(await db.Accruals.ToArrayAsync());

        var october = await service.CreateMeterReadingAsync(new(garage.Id, MeterKinds.Electricity,
            new(2026, 10, 1), new(2026, 10, 28), 1540, "Synthetic later input"), null, default);
        Assert.True(october.Succeeded, october.ErrorMessage);
        Assert.Equal((1160m, 380m, seed.Current.Id), (october.Value!.PreviousValue, october.Value.Consumption, october.Value.MeterDeviceId));
        Assert.Equal(980m, (await service.GetStartValuesAsync(garage, default)).Single(item => item.MeterKind == MeterKinds.Electricity).Value);
        // The normal reading input creates its metered accrual; cleanup itself created none.
        Assert.Equal(760m, (await db.Accruals.SingleAsync(item => item.GarageId == garage.Id && !item.IsCanceled)).Amount);

        // A real replacement still starts a new segment at zero; it never inherits opening980.
        var replacement = await service.ReplaceMeterDeviceAsync(new(garage.Id, MeterKinds.Electricity,
            new(2026, 11, 1), new(2026, 11, 10), "SYNTHETIC-PHYSICAL", 0, 5, 1600, "Synthetic replacement"), null, default);
        Assert.True(replacement.Succeeded, replacement.ErrorMessage);
        Assert.Equal(65m, replacement.Value!.Reading.Consumption);
        var details = (await service.GetMeterDeviceReplacementAsync(replacement.Value.Device.Id, default)).Value!;
        var edit = await service.UpdateMeterDeviceReplacementAsync(details.Device.Id,
            new(details.Device.Version, details.RemovedDevice.Version, details.Reading!.Version, new(2026, 11, 1),
                new(2026, 11, 11), "SYNTHETIC-PHYSICAL-EDITED", 1601, 0, "Synthetic correction"), null, default);
        Assert.True(edit.Succeeded, edit.ErrorMessage);
        Assert.Equal(66m, edit.Value!.Reading!.Consumption);
        Assert.Equal(980m, (await service.GetStartValuesAsync(garage, default)).Single(item => item.MeterKind == MeterKinds.Electricity).Value);
        Assert.Equal(53547m, (await service.GetStartValuesAsync(await db.Garages.SingleAsync(item => item.Id == seed.Other.Id), default))
            .Single(item => item.MeterKind == MeterKinds.Electricity).Value);
        Assert.Equal((980m, new DateOnly(2026, 7, 1)), (garage.InitialElectricityMeterValue, garage.InitialMeterReadingMonth));
    }

    [Fact]
    public void PreservedSelection_WinsAfterNewFirstReadingAndRejectsAmbiguousPins()
    {
        var old = Device(new Garage { Number = "SYNTHETIC" }, "OLD", 980, 8);
        old.IsAccountingBaseline = true;
        var current = Device(old.Garage, "Начало учёта 01.10.2026", 1160, 10);
        var reading = new MeterReading { MeterKind = MeterKinds.Electricity, MeterDeviceId = current.Id };
        Assert.Same(old, MeterBaselineResolver.FindBaselineDevice([old, current], reading));
        Assert.Same(old, MeterBaselineResolver.FindBaselineDevice([old, current], null));
        current.IsAccountingBaseline = true;
        Assert.Throws<InvalidOperationException>(() => MeterBaselineResolver.FindBaselineDevice([old, current], null));
        old.IsCanceled = true;
        Assert.Same(current, MeterBaselineResolver.FindBaselineDevice([old, current], null));
    }

    [Fact]
    public async Task ExplicitStartCorrection_UpdatesPreservedOpeningWithoutChangingCurrentSegment()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var db = database.Context;
        var seed = await SeedAsync(db);
        seed.Original.IsAccountingBaseline = true;
        await db.SaveChangesAsync();
        await db.MeterReadings.ExecuteDeleteAsync();
        var service = FinanceServiceTestFactory.Create(db, businessDate: new(2026, 11, 20));
        var result = await service.ApplyStartValuesAsync(seed.Garage, [new(MeterKinds.Electricity, 990)], null, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var locks = result.Value!;
        await db.SaveChangesAsync();
        Assert.Equal(990m, seed.Original.InitialValue);
        Assert.Equal(990m, seed.Garage.InitialElectricityMeterValue);
        Assert.Equal(1160m, seed.Current.InitialValue);
        Assert.Equal(990m, (await service.GetStartValuesAsync(seed.Garage, default)).Single(item => item.MeterKind == MeterKinds.Electricity).Value);
    }

    internal static async Task<Seed> SeedAsync(GarageBalanceDbContext db)
    {
        foreach (var setting in await db.ChargeServiceSettings.ToArrayAsync()) setting.IsArchived = true;
        var garage = new Garage
        {
            Number = "SYNTHETIC-BASELINE-A",
            AccountingStartMonth = new(2026, 8, 1),
            InitialMeterReadingMonth = new(2026, 7, 1),
            RegisteredOn = new(2026, 8, 1),
            InitialElectricityMeterValue = 980,
            StartingBalance = 6500,
            StartingOverdueDebt = 6500
        };
        var other = new Garage
        {
            Number = "SYNTHETIC-BASELINE-B",
            AccountingStartMonth = new(2026, 8, 1),
            InitialMeterReadingMonth = new(2026, 7, 1),
            RegisteredOn = new(2026, 8, 1),
            InitialElectricityMeterValue = 53547
        };
        var original = Device(garage, "SYNTHETIC-ORIGINAL", 980, 8);
        original.RemovedOn = new(2026, 9, 30);
        original.FinalValue = 4578;
        var current = Device(garage, "Начало учёта SYNTHETIC", 1160, 10);
        var otherDevice = Device(other, "Начало учёта SYNTHETIC-B", 53547, 10);
        var otherOld = Device(other, "SYNTHETIC-OTHER-OLD", 48693, 9);
        otherOld.InstalledOn = new(2026, 9, 18);
        otherOld.RemovedOn = new(2026, 9, 30);
        otherOld.FinalValue = 49361;
        var income = new IncomeType { Name = "Synthetic preserved electricity" };
        var tariff = new Tariff { Name = "Synthetic preserved rate", CalculationBase = TariffCalculationBases.MeterElectricity, Rate = 2, EffectiveFrom = new(2026, 1, 1) };
        db.AddRange(garage, other, original, current, otherDevice, otherOld, income, tariff,
            new ChargeServiceSetting
            {
                Name = "Synthetic metered service",
                IncomeType = income,
                Tariff = tariff,
                IsRegular = true,
                IsMetered = true,
                MeterKind = MeterKinds.Electricity,
                UnitName = "kWh",
                PeriodicityMonths = 1,
                AccrualStartMonth = 1,
                PaymentDueDay = 20
            });
        Add(garage, original, 8, 980, 1200); Add(garage, original, 9, 1200, 1420); Add(garage, current, 10, 1160, 1540);
        Add(other, otherDevice, 8, 53547, 53547); Add(other, otherDevice, 9, 53547, 53782); Add(other, otherDevice, 10, 53782, 53782);
        await db.SaveChangesAsync();
        return new(garage, other, original, current, otherDevice, income, tariff);
        void Add(Garage g, MeterDevice d, int month, decimal previous, decimal value) => db.Add(new MeterReading
        {
            Garage = g,
            MeterKind = MeterKinds.Electricity,
            MeterDevice = d,
            AccountingMonth = new(2026, month, 1),
            ReadingDate = new(2026, month, 1),
            CurrentValue = value,
            PreviousValue = previous,
            Consumption = value - previous
        });
    }

    private static MeterDevice Device(Garage garage, string serial, decimal value, int month) =>
        new() { Garage = garage, GarageId = garage.Id, MeterKind = MeterKinds.Electricity, SerialNumber = serial, InitialValue = value, InstalledOn = new(2026, month, 1) };
    internal sealed record Seed(Garage Garage, Garage Other, MeterDevice Original, MeterDevice Current, MeterDevice OtherDevice, IncomeType Income, Tariff Tariff);
}
