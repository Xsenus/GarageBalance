using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Maintenance;

// Customer comment 6 (14.10.2026) and the decision of 21.10.2026: after the replacement tests
// the «Показания» form is cleared — readings and replacements go, start values stay, and money
// paid for reading-based charges stays as an advance of the same service.
public sealed class MeterHistoryCleanupTests
{
    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-administrator");
    private const string Reason = "Synthetic meter history cleanup";
    private const string FormulaJson = """{"version":1,"totalAmount":50}""";

    [Fact]
    public async Task Sqlite_ClearsReadingsAndReplacementsKeepsStartValuesAndTurnsPaymentsIntoAdvance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertCleanupAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ClearsReadingsAndReplacementsKeepsStartValuesAndTurnsPaymentsIntoAdvance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertCleanupAsync(context);
    }

    private static async Task AssertCleanupAsync(GarageBalanceDbContext context)
    {
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new MeterHistoryCleanupEngine(context, new AcceptBackup(), TimeProvider.System);

        var plan = await engine.PreviewAsync(CancellationToken.None);
        Assert.Equal((2, 4, 1, 1, 1, 1, 1, 50m, 50m), (plan.GarageCount, plan.ReadingCount, plan.KeptStartReadingCount, plan.ReplacementDeviceCount,
            plan.ReactivatedDeviceCount, plan.MeteredAccrualCount, plan.PaidMeteredAccrualCount, plan.MeteredAccrualTotal, plan.PaidMeteredTotal));

        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);

        Assert.Equal((4, 1, 1, 1, 1), (result.DeletedReadings, result.DeletedReplacementDevices, result.ReactivatedDevices, result.CanceledAccruals, result.RebuiltAllocationKeys));
        // A meter without any other start source keeps its earliest reading as the start value.
        var remaining = Assert.Single(await context.MeterReadings.AsNoTracking().ToListAsync());
        Assert.Equal((seed.Legacy.Id, September.AddMonths(-2), 980m), (remaining.GarageId, remaining.AccountingMonth, remaining.CurrentValue));
        Assert.False(await context.MeterDevices.AnyAsync(device => device.Id == seed.Replacement.Id));
        var original = await context.MeterDevices.AsNoTracking().SingleAsync(device => device.Id == seed.Original.Id);
        Assert.Equal((null, null, 100m), (original.RemovedOn, original.FinalValue, original.InitialValue));
        // The «Начало учёта» boundary and the device it closed keep their state.
        var boundary = await context.MeterDevices.AsNoTracking().SingleAsync(device => device.Id == seed.Boundary.Id);
        Assert.Null(boundary.RemovedOn);
        Assert.Equal(October.AddDays(-1), (await context.MeterDevices.AsNoTracking().SingleAsync(device => device.Id == seed.Closed.Id)).RemovedOn);
        Assert.True((await context.Accruals.AsNoTracking().SingleAsync(accrual => accrual.Id == seed.Metered.Id)).IsCanceled);
        Assert.False((await context.Accruals.AsNoTracking().SingleAsync(accrual => accrual.Id == seed.Historical.Id)).IsCanceled);
        Assert.False((await context.Accruals.AsNoTracking().SingleAsync(accrual => accrual.Id == seed.Fixed.Id)).IsCanceled);
        // The payment stays and is no longer applied to the cancelled charge: it becomes an advance.
        Assert.True(await context.FinancialOperations.AnyAsync(operation => operation.Id == seed.Payment.Id && !operation.IsCanceled));
        Assert.False(await context.AccrualPaymentAllocations.AnyAsync(allocation => allocation.IsActive && allocation.AccrualId == seed.Metered.Id));
        var garageAudit = await context.AuditEvents.SingleAsync(item => item.Action == "finance.meter_history_cleared" && item.RelatedGarageId == seed.Garage.Id.ToString());
        Assert.Equal(seed.Garage.Id.ToString(), garageAudit.RelatedGarageId);
        Assert.Contains("показаний 3", garageAudit.Summary, StringComparison.Ordinal);
        Assert.True(await context.AuditEvents.AnyAsync(item => item.Action == "finance.meter_history_cleanup"));
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Execute_RejectsAStalePreviewAndChangesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new MeterHistoryCleanupEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);
        await context.MeterReadings.Where(reading => reading.AccountingMonth == September)
            .ExecuteUpdateAsync(setters => setters.SetProperty(reading => reading.Version, Guid.NewGuid()));

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.Equal(5, await context.MeterReadings.CountAsync());
        Assert.True(await context.MeterDevices.AnyAsync(device => device.Id == seed.Replacement.Id));
        Assert.False((await context.Accruals.AsNoTracking().SingleAsync(accrual => accrual.Id == seed.Metered.Id)).IsCanceled);
    }

    [Theory]
    [InlineData(false, true, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, false, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, true, "", "cleanup_reason_required")]
    public async Task Execute_RequiresHostPrivilegeStoppedApplicationAndReason(bool privileged, bool stopped, string reason, string code)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var engine = new MeterHistoryCleanupEngine(database.Context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan),
            new(privileged, stopped, "synthetic-administrator"), reason, CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(5, await database.Context.MeterReadings.CountAsync());
    }

    [Fact]
    public async Task Preview_RefusesAReplacementThatIsTheStartOfAConnection()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        database.Context.Add(new GarageMeterConnection { GarageId = seed.Garage.Id, MeterKind = MeterKinds.Water, ConnectedOn = October, BaselineMeterDeviceId = seed.Replacement.Id });
        await database.Context.SaveChangesAsync();
        var engine = new MeterHistoryCleanupEngine(database.Context, new AcceptBackup(), TimeProvider.System);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(CancellationToken.None));

        Assert.Equal("cleanup_replacement_is_baseline", error.Code);
    }

    private static AccountingCleanupBackupReceipt Receipt(MeterHistoryCleanupPlan plan) =>
        new(1, plan.Source, "synthetic-private-backup", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private static async Task<(Garage Garage, Garage Legacy, MeterDevice Original, MeterDevice Replacement, MeterDevice Closed, MeterDevice Boundary,
        Accrual Metered, Accrual Historical, Accrual Fixed, FinancialOperation Payment)> SeedAsync(GarageBalanceDbContext context)
    {
        var fund = new Fund { Name = "Synthetic meter fund", NormalizedName = "SYNTHETIC METER FUND", IsSystem = false };
        var water = new IncomeType { Name = "Synthetic water", Code = "synthetic_water", DestinationFund = fund };
        var electricity = new IncomeType { Name = "Synthetic electricity", Code = "synthetic_electricity", DestinationFund = fund };
        var membership = new IncomeType { Name = "Synthetic membership", Code = "synthetic_membership", DestinationFund = fund };
        var garage = new Garage { Number = "synthetic-meters", PeopleCount = 1, FloorCount = 1 };
        var original = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Water, SerialNumber = "SYNTHETIC-OLD", InitialValue = 100, InstalledOn = September.AddDays(-1), RemovedOn = October.AddDays(4), FinalValue = 130 };
        var replacement = new MeterDevice
        {
            Garage = garage,
            MeterKind = MeterKinds.Water,
            SerialNumber = "SYNTHETIC-NEW",
            InitialValue = 500,
            InstalledOn = October.AddDays(5),
            ReplacedDeviceId = original.Id,
            ReplacementAccountingMonth = October,
            HasReplacementSnapshot = true,
            ReplacementReason = "Synthetic test"
        };
        var closed = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = "SYNTHETIC-E", InitialValue = 0, InstalledOn = September.AddDays(-1), RemovedOn = October.AddDays(-1), FinalValue = 40 };
        var boundary = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Electricity, SerialNumber = $"{MeterBaselineResolver.RestartDeviceSerialPrefix} 01.10.2026", InitialValue = 0, InstalledOn = October };
        var metered = new Accrual { Garage = garage, IncomeType = water, AccountingMonth = October, Amount = 50m, Source = AccrualSources.Regular, CalculationMeterKind = MeterKinds.Water, CalculationDetailsJson = FormulaJson };
        var historical = new Accrual { Garage = garage, IncomeType = electricity, AccountingMonth = September, Amount = 70m, Source = AccrualSources.Regular, CalculationMeterKind = MeterKinds.Electricity, CalculationDetailsJson = """{"version":0,"totalAmount":70}""" };
        var fixedAccrual = new Accrual { Garage = garage, IncomeType = membership, AccountingMonth = October, Amount = 500m, Source = AccrualSources.Regular };
        var payment = new FinancialOperation { Garage = garage, IncomeType = water, OperationKind = FinancialOperationKinds.Income, OperationDate = October, AccountingMonth = October, Amount = 50m };
        var legacy = new Garage { Number = "synthetic-legacy-meters", PeopleCount = 1, FloorCount = 1 };
        context.AddRange(legacy,
            new MeterReading { Garage = legacy, MeterKind = MeterKinds.Electricity, AccountingMonth = September.AddMonths(-2), ReadingDate = September.AddMonths(-1).AddDays(-1), CurrentValue = 980 },
            new MeterReading { Garage = legacy, MeterKind = MeterKinds.Electricity, AccountingMonth = September.AddMonths(-1), ReadingDate = September.AddDays(-1), CurrentValue = 1035, PreviousValue = 980, Consumption = 55 });
        context.AddRange(garage, original, replacement, closed, boundary, metered, historical, fixedAccrual, payment,
            new AccrualPaymentAllocation { FinancialOperation = payment, Accrual = metered, Amount = 50m },
            new MeterReading { Garage = garage, MeterDevice = original, MeterKind = MeterKinds.Water, AccountingMonth = September, ReadingDate = September.AddDays(29), CurrentValue = 110, PreviousValue = 100, Consumption = 10 },
            new MeterReading { Garage = garage, MeterDevice = replacement, MeterKind = MeterKinds.Water, AccountingMonth = October, ReadingDate = October.AddDays(5), CurrentValue = 550, PreviousValue = 110, Consumption = 70, IsMeterReplacement = true },
            new MeterReading { Garage = garage, MeterDevice = closed, MeterKind = MeterKinds.Electricity, AccountingMonth = September, ReadingDate = September.AddDays(29), CurrentValue = 40, IsCanceled = true });
        await context.SaveChangesAsync();
        return (garage, legacy, original, replacement, closed, boundary, metered, historical, fixedAccrual, payment);
    }

    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
