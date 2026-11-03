using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// Synthetic-only data matching the focused preservation/cleanup regression cases.
internal static class FixtureData
{
    internal static async Task<Fund> SeedFinancialHistoryAsync(GarageBalanceDbContext db, Seed seed)
    {
        var fund = new Fund { Name = "Synthetic cleanup fund", NormalizedName = "SYNTHETIC CLEANUP FUND", Balance = 101 };
        var supplier = new Supplier { Name = "Synthetic preserved supplier", Group = new SupplierGroup { Name = "Synthetic preserved group" } };
        var expense = new ExpenseType { Name = "Synthetic supplier accrual" };
        var target = new Accrual { Garage = seed.Garage, IncomeType = seed.Income, AccountingMonth = new(2026, 8, 1), Amount = 120, Source = "regular" };
        var credit = new Accrual { Garage = seed.Garage, IncomeType = seed.Income, AccountingMonth = new(2026, 8, 1), Amount = -20, Source = "manual" };
        var payment = new FinancialOperation
        {
            OperationKind = "income",
            Garage = seed.Garage,
            IncomeType = seed.Income,
            Amount = 100,
            OperationDate = new(2026, 8, 20),
            AccountingMonth = new(2026, 8, 1),
            TargetAccrual = target
        };
        db.AddRange(fund, supplier, expense, target, credit, payment,
            new AccrualPaymentAllocation { Accrual = target, FinancialOperation = payment, Amount = 100 },
            new AccrualPaymentAllocation { Accrual = target, CreditAccrual = credit, Amount = 20 },
            new FundOperation { Fund = fund, OperationKind = "deposit", SourceFinancialOperation = payment, Amount = 100 },
            new FundOperation { Fund = fund, OperationKind = "withdraw", Amount = 10 },
            new FundOperation { Fund = fund, OperationKind = "deposit", Amount = 10 },
            new FundOperation { Fund = fund, OperationKind = "deposit", Amount = 1 },
            new FundOperation { Fund = fund, OperationKind = "withdraw", Amount = 1, IsCanceled = true });
        for (var index = 0; index < 4; index++) db.Add(new SupplierAccrual
        {
            Supplier = supplier,
            ExpenseType = expense,
            AccountingMonth = new(2026, 8, 1),
            Amount = 100 + index,
            Source = "manual"
        });
        await db.SaveChangesAsync();
        return fund;
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
