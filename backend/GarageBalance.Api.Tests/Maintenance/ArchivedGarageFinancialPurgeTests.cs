using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Maintenance;

// Customer comment 4 (14.10.2026) and the decision of 21.10.2026: a deleted test garage is
// removed together with its accruals and payments, and its money leaves the funds.
public sealed class ArchivedGarageFinancialPurgeTests
{
    private static readonly DateOnly Month = new(2026, 10, 1);
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-administrator");
    private const string Reason = "Synthetic deleted garage purge";

    [Fact]
    public async Task Sqlite_PurgesArchivedGarageHistoryAndRecalculatesTheFundChain()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertPurgeAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PurgesArchivedGarageHistoryAndRecalculatesTheFundChain()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertPurgeAsync(context);
    }

    private static async Task AssertPurgeAsync(GarageBalanceDbContext context)
    {
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new ArchivedGarageFinancialPurgeEngine(context, new AcceptBackup(), TimeProvider.System);

        var plan = await engine.PreviewAsync(CancellationToken.None);
        var candidate = Assert.Single(plan.Garages);
        Assert.Equal(seed.Archived.Id, candidate.GarageId);
        Assert.Equal((2, 2, 1, 1, 1, 100m), (candidate.AccrualCount, candidate.OperationCount, candidate.FundOperationCount,
            candidate.MeterReadingCount, candidate.MeterDeviceCount, candidate.ActiveIncomeTotal));
        Assert.Equal(["synthetic-scoped"], plan.BlockedGarageNumbers);

        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);

        Assert.Equal((1, 2, 2, 1, 100m), (result.DeletedGarages, result.DeletedAccruals, result.DeletedOperations, result.RemovedFundOperations, result.RemovedIncomeTotal));
        Assert.False(await context.Garages.AnyAsync(garage => garage.Id == seed.Archived.Id));
        Assert.False(await context.Accruals.AnyAsync(accrual => accrual.GarageId == seed.Archived.Id));
        Assert.False(await context.FinancialOperations.AnyAsync(operation => operation.GarageId == seed.Archived.Id));
        Assert.False(await context.MeterReadings.AnyAsync(reading => reading.GarageId == seed.Archived.Id));
        Assert.False(await context.MeterDevices.AnyAsync(device => device.GarageId == seed.Archived.Id));
        Assert.True(await context.Garages.AnyAsync(garage => garage.Id == seed.Scoped.Id));
        Assert.Equal(1, await context.FinancialOperations.CountAsync(operation => operation.GarageId == seed.Active.Id));
        // The removed deposit disappears from the fund; the later withdrawal keeps a consistent chain.
        var fund = await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Fund.Id);
        Assert.Equal(950m, fund.Balance);
        var history = (await context.FundOperations.AsNoTracking().Where(item => item.FundId == seed.Fund.Id).ToListAsync())
            .OrderBy(item => item.CreatedAtUtc).Select(item => (item.BalanceBefore, item.BalanceAfter)).ToArray();
        Assert.Equal([(0m, 1000m), (1000m, 950m)], history);
        var purged = await context.AuditEvents.SingleAsync(item => item.Action == "dictionary.garage_purged_with_financial_history");
        Assert.Equal(seed.Archived.Id.ToString(), purged.RelatedGarageId);
        Assert.Contains("100.00", purged.Summary, StringComparison.Ordinal);
        Assert.True(await context.AuditEvents.AnyAsync(item => item.Action == "fund.operations_removed_with_archived_garage" && item.EntityId == seed.Fund.Id.ToString()));
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Execute_RejectsAStalePreviewAndChangesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new ArchivedGarageFinancialPurgeEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);
        await context.Accruals.Where(accrual => accrual.GarageId == seed.Archived.Id && !accrual.IsCanceled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(accrual => accrual.Amount, 120m));

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.True(await context.Garages.AnyAsync(garage => garage.Id == seed.Archived.Id));
        Assert.Equal(1050m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Fund.Id)).Balance);
        Assert.False(await context.AuditEvents.AnyAsync(item => item.Action == "dictionary.garage_purged_with_financial_history"));
    }

    [Theory]
    [InlineData(false, true, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, false, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, true, " ", "cleanup_reason_required")]
    public async Task Execute_RequiresHostPrivilegeStoppedApplicationAndReason(bool privileged, bool stopped, string reason, string code)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var engine = new ArchivedGarageFinancialPurgeEngine(database.Context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan),
            new(privileged, stopped, "synthetic-administrator"), reason, CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(3, await database.Context.Garages.CountAsync());
    }

    [Fact]
    public async Task Execute_RejectsAChangedFingerprint()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var engine = new ArchivedGarageFinancialPurgeEngine(database.Context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, new string('0', 64), Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_plan_invalid", error.Code);
    }

    // The deleted fund had returned its remainder (including the removed payment) to cash/bank;
    // the purge keeps it at zero by shrinking that return, so cash/bank and funds stay reconciled.
    [Theory]
    [InlineData(1000, 1000, false)]
    [InlineData(0, 0, true)]
    public async Task Execute_KeepsADeletedFundAtZeroByCorrectingItsRemainderReturn(int opening, int expectedRemainder, bool remainderCanceled)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, fund) = await SeedDeletedFundAsync(context, opening, sourcedFinalWithdrawal: false);
        context.ChangeTracker.Clear();
        var engine = new ArchivedGarageFinancialPurgeEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);

        Assert.Equal(1, result.DeletedGarages);
        Assert.False(await context.Garages.AnyAsync(item => item.Id == garage.Id));
        Assert.Equal(0m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
        var remainder = (await context.FundOperations.AsNoTracking().Where(item => item.FundId == fund.Id).ToListAsync())
            .OrderBy(item => item.CreatedAtUtc).Last();
        Assert.Equal(FundOperationKinds.Withdraw, remainder.OperationKind);
        Assert.Equal(remainderCanceled, remainder.IsCanceled);
        Assert.Equal(remainderCanceled ? 100m : expectedRemainder, remainder.Amount);
        Assert.Equal(0m, remainder.BalanceAfter);
        var audit = await context.AuditEvents.SingleAsync(item => item.Action == "fund.operations_removed_with_archived_garage");
        Assert.Contains("возврат остатка", audit.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_RefusesADeletedFundWithoutARemainderReturnAndChangesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var (garage, fund) = await SeedDeletedFundAsync(context, 0, sourcedFinalWithdrawal: true);
        context.ChangeTracker.Clear();
        var engine = new ArchivedGarageFinancialPurgeEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_archived_fund_unbalanced", error.Code);
        Assert.True(await context.Garages.AnyAsync(item => item.Id == garage.Id));
        Assert.Equal(2, await context.FundOperations.CountAsync(item => item.FundId == fund.Id));
        Assert.Equal(0m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
    }

    private static async Task<(Garage Garage, Fund Fund)> SeedDeletedFundAsync(GarageBalanceDbContext context, decimal opening, bool sourcedFinalWithdrawal)
    {
        var fund = new Fund { Name = "Synthetic deleted fund", NormalizedName = "SYNTHETIC DELETED FUND", IsArchived = true, Balance = 0m };
        var income = new IncomeType { Name = "Synthetic deleted fund income", Code = "synthetic_deleted_fund" };
        var garage = new Garage { Number = "synthetic-deleted-fund", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
        var payment = new FinancialOperation { Garage = garage, IncomeType = income, OperationKind = FinancialOperationKinds.Income, OperationDate = Month, AccountingMonth = Month, Amount = 100m };
        var expense = new FinancialOperation { OperationKind = FinancialOperationKinds.Expense, OperationDate = Month, AccountingMonth = Month, Amount = opening + 100m };
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        context.AddRange(fund, garage, payment,
            new FundOperation { Fund = fund, SourceFinancialOperation = payment, OperationKind = FundOperationKinds.Deposit, Amount = 100m, BalanceBefore = opening, BalanceAfter = opening + 100m, Reason = "Synthetic payment", CreatedAtUtc = start.AddHours(1) },
            new FundOperation { Fund = fund, SourceFinancialOperation = sourcedFinalWithdrawal ? expense : null, OperationKind = FundOperationKinds.Withdraw, Amount = opening + 100m, BalanceBefore = opening + 100m, BalanceAfter = 0m, Reason = "Synthetic remainder return", CreatedAtUtc = start.AddHours(2) });
        if (opening > 0m)
            context.Add(new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Deposit, Amount = opening, BalanceBefore = 0m, BalanceAfter = opening, Reason = "Synthetic opening", CreatedAtUtc = start });
        if (sourcedFinalWithdrawal) context.Add(expense);
        await context.SaveChangesAsync();
        return (garage, fund);
    }

    private static AccountingCleanupBackupReceipt Receipt(ArchivedGarageFinancialPurgePlan plan) =>
        new(1, plan.Source, "synthetic-private-backup", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private static async Task<(Garage Archived, Garage Active, Garage Scoped, Fund Fund)> SeedAsync(GarageBalanceDbContext context)
    {
        var fund = new Fund { Name = "Synthetic purge fund", NormalizedName = "SYNTHETIC PURGE FUND", IsSystem = false, Balance = 1050m };
        var income = new IncomeType { Name = "Synthetic purge", Code = "synthetic_purge", DestinationFund = fund };
        var archived = new Garage { Number = "synthetic-deleted", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
        var active = new Garage { Number = "synthetic-active", PeopleCount = 1, FloorCount = 1 };
        var scoped = new Garage { Number = "synthetic-scoped", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
        var accrual = new Accrual { Garage = archived, IncomeType = income, AccountingMonth = Month, Amount = 100m, Source = AccrualSources.Regular };
        var canceled = new Accrual { Garage = archived, IncomeType = income, AccountingMonth = Month, Amount = 30m, Source = AccrualSources.Regular, IsCanceled = true };
        var payment = new FinancialOperation { Garage = archived, IncomeType = income, OperationKind = FinancialOperationKinds.Income, OperationDate = Month, AccountingMonth = Month, Amount = 100m };
        var canceledPayment = new FinancialOperation { Garage = archived, IncomeType = income, OperationKind = FinancialOperationKinds.Income, OperationDate = Month, AccountingMonth = Month, Amount = 5m, IsCanceled = true };
        var otherPayment = new FinancialOperation { Garage = active, IncomeType = income, OperationKind = FinancialOperationKinds.Income, OperationDate = Month, AccountingMonth = Month, Amount = 10m };
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var device = new MeterDevice { Garage = archived, MeterKind = MeterKinds.Water, SerialNumber = "SYNTHETIC", InitialValue = 0, InstalledOn = Month };
        context.AddRange(fund, archived, active, scoped, accrual, canceled, payment, canceledPayment, otherPayment, device,
            new AccrualPaymentAllocation { FinancialOperation = payment, Accrual = accrual, Amount = 100m },
            new MeterReading { Garage = archived, MeterDevice = device, MeterKind = MeterKinds.Water, AccountingMonth = Month, ReadingDate = Month, CurrentValue = 5, Consumption = 5 },
            new GaragePeopleCountPeriod { GarageId = archived.Id, EffectiveFrom = Month, PeopleCount = 1 },
            new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Deposit, Amount = 1000m, BalanceBefore = 0m, BalanceAfter = 1000m, Reason = "Synthetic opening", CreatedAtUtc = start },
            new FundOperation { Fund = fund, SourceFinancialOperation = payment, OperationKind = FundOperationKinds.Deposit, Amount = 100m, BalanceBefore = 1000m, BalanceAfter = 1100m, Reason = "Synthetic payment", CreatedAtUtc = start.AddHours(1) },
            new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Withdraw, Amount = 50m, BalanceBefore = 1100m, BalanceAfter = 1050m, Reason = "Synthetic spending", CreatedAtUtc = start.AddHours(2) },
            new ChargeServiceSetting { Name = "Synthetic scoped purge", AppliesToSelectedGarages = true, GarageIds = [scoped.Id] });
        await context.SaveChangesAsync();
        return (archived, active, scoped, fund);
    }

    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
