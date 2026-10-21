using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Maintenance;

// Repairs a deleted fund left with a non-zero balance after its history was changed outside the
// application (staging, 21.10.2026): the "remainder returned on deletion" absorbs the difference.
public sealed class ArchivedFundBalanceRepairTests
{
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-administrator");
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Reason = "Synthetic archived fund repair";

    [Fact]
    public async Task Sqlite_ReturnsTheDeletedFundToZeroThroughItsRemainderReturn()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertRepairAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ReturnsTheDeletedFundToZeroThroughItsRemainderReturn()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertRepairAsync(context);
    }

    private static async Task AssertRepairAsync(GarageBalanceDbContext context)
    {
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new ArchivedFundBalanceRepairEngine(context, new AcceptBackup(), TimeProvider.System);

        var plan = await engine.PreviewAsync(CancellationToken.None);

        var candidate = Assert.Single(plan.Funds);
        Assert.Equal((seed.Broken.Id, -100m, 1100m, 1000m), (candidate.FundId, candidate.Balance, candidate.RemainderAmount, candidate.RepairedRemainderAmount));
        Assert.Equal(["Synthetic unrepairable fund"], plan.BlockedFundNames);

        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);

        Assert.Equal(1, result.RepairedFunds);
        Assert.Equal(0m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Broken.Id)).Balance);
        var history = (await context.FundOperations.AsNoTracking().Where(item => item.FundId == seed.Broken.Id).ToListAsync())
            .OrderBy(item => item.CreatedAtUtc).Select(item => (item.Amount, item.BalanceBefore, item.BalanceAfter, item.IsCanceled)).ToArray();
        Assert.Equal([(1000m, 0m, 1000m, false), (1000m, 1000m, 0m, false), (5m, 0m, 0m, true)], history);
        // A fund that cannot be repaired this way and an active fund are left as they were.
        Assert.Equal(-50m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Unrepairable.Id)).Balance);
        Assert.Equal(300m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Active.Id)).Balance);
        var audit = await context.AuditEvents.SingleAsync(item => item.Action == "fund.archived_balance_repaired");
        Assert.Equal(seed.Broken.Id.ToString(), audit.EntityId);
        Assert.Contains("1100.00 → 1000.00", audit.Summary, StringComparison.Ordinal);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Empty((await engine.PreviewAsync(CancellationToken.None)).Funds);
    }

    [Fact]
    public async Task Execute_CancelsARemainderReturnThatDropsToZero()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var fund = new Fund { Name = "Synthetic emptied fund", NormalizedName = "SYNTHETIC EMPTIED FUND", IsArchived = true, Balance = -40m };
        context.AddRange(fund, new FundOperation { Fund = fund, OperationKind = FundOperationKinds.Withdraw, Amount = 40m, BalanceBefore = 0m, BalanceAfter = -40m, Reason = "Synthetic remainder return", CreatedAtUtc = Start });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var engine = new ArchivedFundBalanceRepairEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);

        var remainder = await context.FundOperations.AsNoTracking().SingleAsync(item => item.FundId == fund.Id);
        Assert.True(remainder.IsCanceled);
        Assert.Equal((40m, 0m, 0m), (remainder.Amount, remainder.BalanceBefore, remainder.BalanceAfter));
        Assert.Equal(0m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == fund.Id)).Balance);
    }

    [Fact]
    public async Task Execute_RejectsAStalePreviewAndChangesNothing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var seed = await SeedAsync(context);
        context.ChangeTracker.Clear();
        var engine = new ArchivedFundBalanceRepairEngine(context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);
        var fund = await context.Funds.SingleAsync(item => item.Id == seed.Broken.Id);
        fund.Version = Guid.NewGuid();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.Equal(-100m, (await context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Broken.Id)).Balance);
        Assert.False(await context.AuditEvents.AnyAsync(item => item.Action == "fund.archived_balance_repaired"));
    }

    [Theory]
    [InlineData(false, true, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, false, "Synthetic reason", "cleanup_permission_denied")]
    [InlineData(true, true, " ", "cleanup_reason_required")]
    public async Task Execute_RequiresHostPrivilegeStoppedApplicationAndReason(bool privileged, bool stopped, string reason, string code)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var engine = new ArchivedFundBalanceRepairEngine(database.Context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan),
            new(privileged, stopped, "synthetic-administrator"), reason, CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(-100m, (await database.Context.Funds.AsNoTracking().SingleAsync(item => item.Id == seed.Broken.Id)).Balance);
    }

    [Fact]
    public async Task Execute_RejectsAChangedFingerprint()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var engine = new ArchivedFundBalanceRepairEngine(database.Context, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, new string('0', 64), Receipt(plan), Identity, Reason, CancellationToken.None));

        Assert.Equal("cleanup_plan_invalid", error.Code);
    }

    private static AccountingCleanupBackupReceipt Receipt(ArchivedFundBalanceRepairPlan plan) =>
        new(1, plan.Source, "synthetic-private-backup", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private static async Task<(Fund Broken, Fund Unrepairable, Fund Active)> SeedAsync(GarageBalanceDbContext context)
    {
        // Broken: opening 1000, the removed payment's 100 is gone, the remainder return still says 1100.
        var broken = new Fund { Name = "Synthetic broken fund", NormalizedName = "SYNTHETIC BROKEN FUND", IsArchived = true, Balance = -100m };
        // Unrepairable: the last active operation is a withdrawal paid for an expense, not a remainder return.
        var unrepairable = new Fund { Name = "Synthetic unrepairable fund", NormalizedName = "SYNTHETIC UNREPAIRABLE FUND", IsArchived = true, Balance = -50m };
        var active = new Fund { Name = "Synthetic active fund", NormalizedName = "SYNTHETIC ACTIVE FUND", Balance = 300m };
        var expense = new FinancialOperation { OperationKind = FinancialOperationKinds.Expense, OperationDate = new DateOnly(2026, 10, 1), AccountingMonth = new DateOnly(2026, 10, 1), Amount = 50m };
        context.AddRange(broken, unrepairable, active, expense,
            new FundOperation { Fund = broken, OperationKind = FundOperationKinds.Deposit, Amount = 1000m, BalanceBefore = 0m, BalanceAfter = 1000m, Reason = "Synthetic opening", CreatedAtUtc = Start },
            new FundOperation { Fund = broken, OperationKind = FundOperationKinds.Withdraw, Amount = 1100m, BalanceBefore = 1000m, BalanceAfter = -100m, Reason = "Synthetic remainder return", CreatedAtUtc = Start.AddHours(1) },
            new FundOperation { Fund = broken, OperationKind = FundOperationKinds.Withdraw, Amount = 5m, BalanceBefore = -100m, BalanceAfter = -100m, Reason = "Synthetic canceled", IsCanceled = true, CreatedAtUtc = Start.AddHours(2) },
            new FundOperation { Fund = unrepairable, SourceFinancialOperation = expense, OperationKind = FundOperationKinds.Withdraw, Amount = 50m, BalanceBefore = 0m, BalanceAfter = -50m, Reason = "Synthetic expense", CreatedAtUtc = Start },
            new FundOperation { Fund = active, OperationKind = FundOperationKinds.Deposit, Amount = 300m, BalanceBefore = 0m, BalanceAfter = 300m, Reason = "Synthetic active", CreatedAtUtc = Start });
        await context.SaveChangesAsync();
        return (broken, unrepairable, active);
    }

    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
