using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Domain.Reports;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Npgsql;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class ArchivedGarageCleanupTests
{
    private static readonly DateOnly Month = new(2026, 10, 1);
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-administrator");
    private const string Reason = "Synthetic authorized archived garage cleanup";

    [Fact]
    public async Task Preview_ProtectsOverdueOpeningBalanceAndDatabaseRejectsImpossibleZeroBalance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var garage = await SeedCleanAsync(database.Context);
        garage.StartingBalance = 2;
        garage.StartingOverdueDebt = 1;
        await database.Context.SaveChangesAsync();
        var plan = await Engine(database.Context).PreviewAsync(100, CancellationToken.None);
        Assert.Empty(plan.EligibleGarages);
        Assert.Equal("opening_balance", Assert.Single(Assert.Single(plan.ProtectedGarages).ProtectionReasons));
        garage.StartingBalance = 0;
        var invalid = await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        Assert.Contains("CK_garages_StartingOverdueDebt", invalid.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execute_RejectsChangedCardVersionWithoutDeletingTheStillArchivedRecord()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedCleanAsync(context);
        var engine = Engine(context);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        garage.Number = "synthetic-changed-card";
        await context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));
        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.Equal(garage.Number, (await context.Garages.AsNoTracking().SingleAsync(row => row.Id == garage.Id)).Number);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Preview_RejectsUnboundedOrEmptyPageLimit(int limit)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => Engine(database.Context).PreviewAsync(limit, CancellationToken.None));
        Assert.Equal("cleanup_limit_invalid", error.Code);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RejectsUnreviewedCascadeDependencyBeforePreviewOrDeletion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = await SeedCleanAsync(context);
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE synthetic_cleanup_unknown (\"GarageId\" uuid REFERENCES garages(\"Id\") ON DELETE CASCADE);");
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => Engine(context).PreviewAsync(100, CancellationToken.None));
        Assert.Equal("cleanup_schema_unreviewed", error.Code);
        Assert.True(await context.Garages.AnyAsync(row => row.Id == garage.Id));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RechecksRestoredGarageAfterBackupVerification()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var garage = await SeedCleanAsync(context);
        context.ChangeTracker.Clear();
        var verifier = new BlockingBackup();
        var engine = Engine(context, verifier);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        var operation = engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);
        try
        {
            await verifier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using var writer = database.CreateContext();
            var restored = await writer.Garages.SingleAsync(row => row.Id == garage.Id);
            restored.IsArchived = false;
            await writer.SaveChangesAsync();
        }
        finally { verifier.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => operation);
        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.False((await context.Garages.AsNoTracking().SingleAsync(row => row.Id == garage.Id)).IsArchived);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RechecksPaymentCommittedWhileParentLockWasWaiting()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        Guid garageId;
        await using (var seed = database.CreateContext()) garageId = (await SeedCleanAsync(seed)).Id;
        var probe = new ParentLockProbe();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(probe).Options);
        var engine = Engine(context);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        await using var writer = database.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var payment = new FinancialOperation
        {
            GarageId = garageId,
            OperationKind = FinancialOperationKinds.Income,
            AccountingMonth = Month,
            OperationDate = Month,
            Amount = 1
        };
        writer.Add(payment);
        await writer.SaveChangesAsync();
        var operation = engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);
        try
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using var observer = new NpgsqlConnection(database.ConnectionString);
            await observer.OpenAsync();
            using var waitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var waiting = false;
            while (!waiting)
            {
                await using var command = observer.CreateCommand();
                command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%FOR UPDATE%');";
                waiting = (bool)(await command.ExecuteScalarAsync(waitDeadline.Token))!;
                if (!waiting) await Task.Delay(10, waitDeadline.Token);
            }
            Assert.False(operation.IsCompleted);
        }
        finally { await transaction.CommitAsync(); }
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => operation);
        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.True(await context.Garages.AnyAsync(row => row.Id == garageId));
        Assert.True(await context.FinancialOperations.AnyAsync(row => row.Id == payment.Id));
    }

    [Fact]
    public async Task Preview_ProtectsAllHistoryIncludingCanceledRecordsAndNeverSelectsActiveGarage()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertPreviewAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PreviewProtectsAllHistoryAndArrayTariffScopes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertPreviewAsync(context);
    }

    private static async Task AssertPreviewAsync(GarageBalanceDbContext context)
    {
        var clean = await SeedProtectedAsync(context);
        var beforeAudit = await context.AuditEvents.CountAsync();
        context.ChangeTracker.Clear();
        var plan = await Engine(context).PreviewAsync(1, CancellationToken.None);
        Assert.Equal(clean.Id, Assert.Single(plan.EligibleGarages).GarageId);
        Assert.False(plan.HasMoreEligible);
        Assert.Equal(1, plan.TotalEligible);
        Assert.Equal(9, plan.TotalArchived);
        Assert.Equal(8, plan.ProtectedGarages.Count);
        Assert.All(plan.ProtectedGarages, row => Assert.Single(row.ProtectionReasons));
        Assert.All(plan.ProtectionCounts, pair => Assert.Equal(1, pair.Value));
        Assert.Equal(plan.Fingerprint, ArchivedGarageCleanupEngine.ComputeFingerprint(plan));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(beforeAudit, await context.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Execute_DeletesOnlyEligibleArchiveRetainsOwnerOtherGarageAuditAndImportProvenance()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await AssertSuccessAsync(database.Context);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExecuteDeletesOnlyConfirmedArchiveAndKeepsAllFinancialRecords()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        await AssertSuccessAsync(context);
    }

    private static async Task AssertSuccessAsync(GarageBalanceDbContext context)
    {
        var clean = await SeedProtectedAsync(context);
        var owner = new Owner { LastName = "Synthetic", FirstName = "Shared" };
        clean.Owner = owner;
        var other = new Garage { Number = "synthetic-active-other", Owner = owner, PeopleCount = 1, FloorCount = 1 };
        var device = new MeterDevice { Garage = clean, MeterKind = MeterKinds.Electricity, SerialNumber = "SYNTHETIC", InitialValue = 0, InstalledOn = Month };
        var quickList = new GarageReportQuickList { Name = "Synthetic cleanup", NormalizedName = "SYNTHETIC CLEANUP" };
        var audit = new AuditEvent { Action = "dictionary.garage_created", EntityType = "garage", EntityId = clean.Id.ToString(), Summary = "Synthetic existing history" };
        var provenance = new AccessImportCreatedRecord { TargetEntityType = "garage", TargetEntityId = clean.Id.ToString() };
        context.AddRange(other, device,
            new GarageMeterConnection { GarageId = clean.Id, MeterKind = MeterKinds.Electricity, ConnectedOn = Month, BaselineMeterDeviceId = device.Id },
            new GaragePeopleCountPeriod { GarageId = clean.Id, EffectiveFrom = Month, PeopleCount = 1 },
            new GarageReportQuickListGarage { Garage = clean, QuickList = quickList }, audit, provenance);
        await context.SaveChangesAsync();
        var operations = await context.FinancialOperations.CountAsync();
        var accruals = await context.Accruals.CountAsync();
        context.ChangeTracker.Clear();
        var verifier = new AcceptBackup();
        var engine = Engine(context, verifier);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        Assert.Equal(1, Assert.Single(plan.EligibleGarages).MeterDeviceCount);
        Assert.Equal(1, plan.EligibleGarages[0].MeterConnectionCount);
        Assert.Equal(1, plan.EligibleGarages[0].QuickListLinkCount);
        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None);
        Assert.Equal(1, result.DeletedGarages);
        Assert.Equal(1, verifier.Calls);
        Assert.False(await context.Garages.AnyAsync(row => row.Id == clean.Id));
        Assert.True(await context.Garages.AnyAsync(row => row.Id == other.Id));
        Assert.True(await context.Owners.AnyAsync(row => row.Id == owner.Id));
        Assert.True(await context.GarageReportQuickLists.AnyAsync(row => row.Id == quickList.Id));
        Assert.False(await context.MeterDevices.AnyAsync(row => row.GarageId == clean.Id));
        Assert.False(await context.GarageMeterConnections.AnyAsync(row => row.GarageId == clean.Id));
        Assert.False(await context.GaragePeopleCountPeriods.AnyAsync(row => row.GarageId == clean.Id));
        Assert.False(await context.GarageReportQuickListGarages.AnyAsync(row => row.GarageId == clean.Id));
        Assert.Equal(operations, await context.FinancialOperations.CountAsync());
        Assert.Equal(accruals, await context.Accruals.CountAsync());
        Assert.True(await context.AuditEvents.AnyAsync(row => row.Id == audit.Id));
        Assert.True(await context.AccessImportCreatedRecords.AnyAsync(row => row.Id == provenance.Id));
        var deletion = await context.AuditEvents.SingleAsync(row => row.Action == "dictionary.garage_purged_without_financial_history");
        Assert.Equal("delete", deletion.ActionKind);
        Assert.Equal(clean.Id.ToString(), deletion.RelatedGarageId);
        Assert.Contains(Reason, deletion.Summary, StringComparison.Ordinal);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_DeniesMissingHostPrivilegeOrRunningApplication(bool privileged, bool stopped)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var clean = await SeedCleanAsync(database.Context);
        var engine = Engine(database.Context);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan),
            new(privileged, stopped, "synthetic"), Reason, CancellationToken.None));
        Assert.Equal("cleanup_permission_denied", error.Code);
        Assert.True(await database.Context.Garages.AnyAsync(row => row.Id == clean.Id));
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("expired")]
    [InlineData("tampered")]
    [InlineData("reason")]
    [InlineData("source")]
    public async Task Execute_RejectsInvalidApprovalReasonOrSourceWithoutWrites(string invalid)
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var clean = await SeedCleanAsync(database.Context);
        var engine = Engine(database.Context);
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        if (invalid == "expired") plan = plan with { CreatedAtUtc = plan.CreatedAtUtc.AddDays(-2) };
        if (invalid == "tampered") plan = plan with { EligibleGarages = [plan.EligibleGarages[0] with { GarageNumber = "changed" }] };
        if (invalid == "source")
        {
            plan = plan with { Source = plan.Source with { DatabaseName = "different-database" } };
            plan = plan with { Fingerprint = ArchivedGarageCleanupEngine.ComputeFingerprint(plan) };
        }
        await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ExecuteAsync(plan, invalid == "fingerprint" ? "incorrect" : plan.Fingerprint,
            Receipt(plan), Identity, invalid == "reason" ? "" : Reason, CancellationToken.None));
        Assert.True(await database.Context.Garages.AnyAsync(row => row.Id == clean.Id));
    }

    [Fact]
    public async Task Execute_RollsBackDeletionAndChildrenWhenAuditSaveFails()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var garage = await SeedCleanAsync(context);
        var device = new MeterDevice { Garage = garage, MeterKind = MeterKinds.Water, SerialNumber = "SYNTHETIC", InstalledOn = Month };
        context.Add(device);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var engine = new ArchivedGarageCleanupEngine(context, new AcceptBackup(), TimeProvider.System, new FailingAudit());
        var plan = await engine.PreviewAsync(100, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, Reason, CancellationToken.None));
        Assert.True(await context.Garages.AnyAsync(row => row.Id == garage.Id));
        Assert.True(await context.MeterDevices.AnyAsync(row => row.Id == device.Id));
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Preview_BoundedBatchAndEmptyExecutionRequireNoBackupOrWrite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var context = database.Context;
        var first = await SeedCleanAsync(context);
        context.Add(new Garage { Number = "synthetic-second", IsArchived = true, PeopleCount = 1, FloorCount = 1 });
        await context.SaveChangesAsync();
        var verifier = new AcceptBackup();
        var engine = Engine(context, verifier);
        var limited = await engine.PreviewAsync(1, CancellationToken.None);
        Assert.True(limited.HasMoreEligible);
        Assert.Equal(2, limited.TotalEligible);
        Assert.Single(limited.EligibleGarages);
        await context.Garages.Where(row => row.IsArchived).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsArchived, false));
        var empty = await engine.PreviewAsync(10, CancellationToken.None);
        Assert.Empty(empty.EligibleGarages);
        Assert.Equal(0, (await engine.ExecuteAsync(empty, empty.Fingerprint, Receipt(empty), Identity, Reason, CancellationToken.None)).DeletedGarages);
        Assert.Equal(0, verifier.Calls);
        Assert.True(await context.Garages.AnyAsync(row => row.Id == first.Id));
    }

    private static ArchivedGarageCleanupEngine Engine(GarageBalanceDbContext context, IAccountingCleanupBackupVerifier? verifier = null) =>
        new(context, verifier ?? new AcceptBackup(), TimeProvider.System);

    private static AccountingCleanupBackupReceipt Receipt(ArchivedGarageCleanupPlan plan) =>
        new(1, plan.Source, "synthetic-private-backup", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private static async Task<Garage> SeedCleanAsync(GarageBalanceDbContext context)
    {
        var garage = new Garage { Number = "synthetic-clean", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
        context.Add(garage);
        await context.SaveChangesAsync();
        return garage;
    }

    private static async Task<Garage> SeedProtectedAsync(GarageBalanceDbContext context)
    {
        var clean = await SeedCleanAsync(context);
        Garage Protected(string name)
        {
            var garage = new Garage { Number = $"synthetic-{name}", IsArchived = true, PeopleCount = 1, FloorCount = 1 };
            context.Add(garage);
            return garage;
        }
        Protected("balance").StartingBalance = 1;
        var paid = Protected("payment");
        context.Add(new FinancialOperation { Garage = paid, OperationKind = FinancialOperationKinds.Income, OperationDate = Month, AccountingMonth = Month, Amount = 1, IsCanceled = true });
        var accrued = Protected("accrual");
        context.Add(new Accrual { Garage = accrued, IncomeType = new IncomeType { Name = "Synthetic cleanup", Code = "synthetic_cleanup" }, AccountingMonth = Month, Amount = 1, Source = AccrualSources.Regular, IsCanceled = true });
        var adjusted = Protected("adjustment");
        context.Add(new OpeningBalanceAdjustment { TargetKind = OpeningBalanceAdjustmentTargetKinds.Garage, TargetId = adjusted.Id, PreviousAmount = 1, NewAmount = 0, EffectiveDate = Month, Reason = "Synthetic history" });
        var read = Protected("reading");
        context.Add(new MeterReading { Garage = read, MeterKind = MeterKinds.Water, AccountingMonth = Month, ReadingDate = Month, CurrentValue = 1, IsCanceled = true });
        var assigned = Protected("assignment");
        var setting = new ChargeServiceSetting { Name = "Synthetic cleanup", IsRegular = true };
        context.Add(new GarageTariffAssignment
        {
            Garage = assigned,
            ChargeServiceSetting = setting,
            Tariff = new Tariff { Name = "Synthetic cleanup", CalculationBase = TariffCalculationBases.Fixed, Rate = 1, EffectiveFrom = Month },
            EffectiveFrom = Month,
            IsArchived = true
        });
        var fee = Protected("fee");
        context.Add(new FeeCampaignGarage
        {
            Garage = fee,
            FeeCampaign = new FeeCampaign
            {
                Name = "Synthetic cleanup",
                ContributionAmount = 1,
                TargetAmount = 1,
                StartsOn = Month,
                IsArchived = true,
                IncomeType = new IncomeType { Name = "Synthetic cleanup fee", Code = "synthetic_cleanup_fee" }
            }
        });
        var scoped = Protected("scope");
        context.Add(new ChargeServiceSetting { Name = "Synthetic scoped cleanup", AppliesToSelectedGarages = true, GarageIds = [scoped.Id] });
        context.Add(new Garage { Number = "synthetic-active", PeopleCount = 1, FloorCount = 1 });
        await context.SaveChangesAsync();
        return clean;
    }

    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public int Calls { get; private set; }
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAudit : IAuditEventWriter
    {
        public AuditEvent? Add(AuditEventWriteRequest request) => throw new InvalidOperationException("Synthetic audit failure");
    }

    private sealed class BlockingBackup : IAccountingCleanupBackupVerifier
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ParentLockProbe : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
