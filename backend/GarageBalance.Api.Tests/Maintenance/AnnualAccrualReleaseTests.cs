using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Tests.Finance;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class AnnualAccrualReleaseTests
{
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-operator");
    private static readonly DateOnly Today = new(2026, 11, 9);

    [PostgreSqlFact]
    public async Task ExactForecastReleaseIsAtomicAuditedAndRecoveryNeverGeneratesCharges()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var engine = Engine(db);
        var plan = await engine.PreviewAsync(2026, default);
        Assert.Equal(2, plan.FormatVersion);
        Assert.Equal(AnnualAccrualHoldReleaseEngine.Operation, plan.OperationKind);
        Assert.True(plan.Forecast!.IsComplete);
        Assert.Equal(1200m, plan.Forecast.NewAccrualAmount);
        Assert.Equal(1, plan.Forecast.NewAccrualCount);
        Assert.Equal(AnnualAccrualReleaseOutcome.RolledBack, await engine.VerifyOutcomeAsync(plan, default));
        await engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic approved release", default);
        Assert.Equal(AnnualAccrualReleaseOutcome.Committed, await engine.VerifyOutcomeAsync(plan, default));
        Assert.Equal(AnnualAccrualReleaseOutcome.Committed, await engine.VerifyOutcomeAsync(plan, default));
        Assert.Empty(await db.Accruals.ToListAsync());
        Assert.False((await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026))).BooleanValue);
        Assert.True((await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2028))).BooleanValue);
        var audit = Assert.Single(await db.AuditEvents.Where(e => e.Action == AnnualAccrualHoldReleaseEngine.AuditAction).ToListAsync());
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(plan.Forecast.InputFingerprint, metadata.RootElement.GetProperty("forecastInputFingerprint").GetString());
        Assert.Equal(plan.Fingerprint, metadata.RootElement.GetProperty("planHash").GetString());
        await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Do not repeat", default));
        Assert.Single(await db.AuditEvents.Where(e => e.Action == AnnualAccrualHoldReleaseEngine.AuditAction).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task ForecastInputAndApprovedAmountDriftBothRejectBeforePolicyChange()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var tariff = await SeedAsync(db);
        var engine = Engine(db);
        var plan = await engine.PreviewAsync(2026, default);
        var forged = plan with { Forecast = plan.Forecast! with { NewAccrualAmount = 1m } };
        forged = forged with { Fingerprint = AnnualAccrualHoldReleaseEngine.Fingerprint(forged) };
        Assert.Equal("cleanup_preview_stale", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ReleaseAsync(forged, forged.Fingerprint, Receipt(forged), Identity, "Synthetic drift", default))).Code);
        await db.Tariffs.Where(t => t.Id == tariff).ExecuteUpdateAsync(s => s.SetProperty(t => t.Rate, 1250m));
        Assert.Equal("cleanup_preview_stale", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic drift", default))).Code);
        Assert.True((await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026))).BooleanValue);
        Assert.Empty(await db.AuditEvents.Where(e => e.Action == AnnualAccrualHoldReleaseEngine.AuditAction).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task UnsupportedForecastAndLegacyPlansCannotAuthorizeRelease()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var tariff = await SeedAsync(db);
        var engine = Engine(db);
        var plan = await engine.PreviewAsync(2026, default);
        var legacy = plan with { FormatVersion = 0, OperationKind = null, Forecast = null };
        legacy = legacy with { Fingerprint = AnnualAccrualHoldReleaseEngine.Fingerprint(legacy) };
        Assert.Throws<AccountingCleanupException>(() => AnnualAccrualHoldReleaseEngine.ValidatePlan(legacy));
        await db.Tariffs.Where(t => t.Id == tariff).ExecuteUpdateAsync(s => s.SetProperty(t => t.CalculationBase, "meter_water"));
        Assert.Equal("cleanup_annual_forecast_incomplete", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(2026, default))).Code);
        Assert.Equal("cleanup_annual_forecast_incomplete", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic unsupported", default))).Code);
        Assert.Empty(await db.Accruals.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task InputWriterCannotChangeApprovedTariffBetweenForecastAndCommit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var tariff = await SeedAsync(db);
        var attempted = false;
        var engine = Engine(db, stopped: async _ =>
        {
            if (attempted) return true;
            attempted = true;
            await using var writer = database.CreateContext();
            await writer.Database.OpenConnectionAsync();
            await writer.Database.ExecuteSqlRawAsync("SET lock_timeout = '200ms'");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.Tariffs.Where(t => t.Id == tariff)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Rate, 9999m)));
            Assert.Equal("55P03", Assert.IsType<PostgresException>(error.InnerException).SqlState);
            return true;
        });
        var plan = await engine.PreviewAsync(2026, default);
        await engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic input lock", default);
        Assert.True(attempted);
        Assert.Equal(1200m, await db.Tariffs.Where(t => t.Id == tariff).Select(t => t.Rate).SingleAsync());
        Assert.Equal(AnnualAccrualReleaseOutcome.Committed, await Engine(db).VerifyOutcomeAsync(plan, default));
    }

    [PostgreSqlFact]
    public async Task OldSnapshotAndLateClientBothPreventReleaseAndAuditFailureRollsBack()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var plan = await Engine(db).PreviewAsync(2026, default);
        foreach (var level in new[] { IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead, IsolationLevel.Serializable })
        {
            await using var oldReader = database.CreateContext();
            await using var snapshot = await oldReader.Database.BeginTransactionAsync(level);
            await oldReader.Database.ExecuteSqlRawAsync("SELECT 1");
            Assert.Equal("cleanup_other_clients_connected", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                Engine(db).ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic old client", default))).Code);
            await snapshot.RollbackAsync();
        }
        await using (var late = database.CreateContext())
        {
            var writer = new CallbackAudit(db, () => late.Database.OpenConnection());
            Assert.Equal("cleanup_other_clients_connected", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                Engine(db, writer).ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic late client", default))).Code);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Engine(db, new CallbackAudit(db, () => throw new InvalidOperationException("synthetic audit failed")))
            .ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic audit failure", default));
        Assert.Equal(AnnualAccrualReleaseOutcome.RolledBack, await Engine(db).VerifyOutcomeAsync(plan, default));
        Assert.Empty(await db.AuditEvents.Where(e => e.Action == AnnualAccrualHoldReleaseEngine.AuditAction).ToListAsync());
    }

    [PostgreSqlFact]
    public async Task OutcomeRequiresExactStateAndUniqueUnmodifiedAuditNotJustMissingEvidence()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var engine = Engine(db);
        var plan = await engine.PreviewAsync(2026, default);
        await db.ApplicationSettings.Where(s => s.Id == plan.SettingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.JsonValue, "{\"drift\":true}"));
        Assert.Equal("cleanup_release_outcome_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyOutcomeAsync(plan, default))).Code);
        plan = await engine.PreviewAsync(2026, default);
        await engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic recovery", default);
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(e => e.Action == AnnualAccrualHoldReleaseEngine.AuditAction);
        var duplicate = new AuditEvent
        {
            Action = audit.Action,
            EntityType = audit.EntityType,
            EntityId = audit.EntityId,
            ActionKind = audit.ActionKind,
            Summary = "Synthetic duplicate evidence",
            MetadataJson = audit.MetadataJson
        };
        db.Add(duplicate);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("cleanup_release_outcome_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyOutcomeAsync(plan, default))).Code);
        await db.AuditEvents.Where(e => e.Id == duplicate.Id).ExecuteDeleteAsync();
        await db.AuditEvents.Where(e => e.Id == audit.Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.MetadataJson, audit.MetadataJson!.Replace(plan.Fingerprint, new string('0', 64))));
        Assert.Equal("cleanup_release_outcome_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyOutcomeAsync(plan, default))).Code);
        await db.AuditEvents.Where(e => e.Id == audit.Id).ExecuteDeleteAsync();
        Assert.Equal("cleanup_release_outcome_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyOutcomeAsync(plan, default))).Code);
        Assert.False((await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Id == plan.SettingId)).BooleanValue);
        Assert.Empty(await db.Accruals.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task FailedOfflineRecheckAndCancellationRollBackBeforeCommit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await SeedAsync(db);
        var calls = 0;
        var engine = Engine(db, stopped: _ => Task.FromResult(++calls == 1));
        var plan = await engine.PreviewAsync(2026, default);
        Assert.Equal("cleanup_host_started", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic restarted", default))).Code);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine(db).ReleaseAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic canceled", canceled.Token));
        Assert.Equal(AnnualAccrualReleaseOutcome.RolledBack, await Engine(db).VerifyOutcomeAsync(plan, default));
    }

    private static AnnualAccrualHoldReleaseEngine Engine(GarageBalanceDbContext db, IAuditEventWriter? writer = null,
        Func<CancellationToken, Task<bool>>? stopped = null) => new(db, new AcceptBackup(), TimeProvider.System,
            writer, stopped, businessDate: new TestBusinessDateProvider(Today));

    private static async Task<Guid> SeedAsync(GarageBalanceDbContext db)
    {
        var (_, service) = await AnnualAccountingStartTests.SeedAsync(db, new(2026, 1, 1));
        db.AddRange(new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2026), BooleanValue = true, IntegerValue = 2026 },
            new ApplicationSetting { Key = AnnualAccrualHoldPolicy.Key(2028), BooleanValue = true, IntegerValue = 2028 });
        (await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch")).JsonValue = "{\"2026\":true,\"2028\":true}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return service.TariffId!.Value;
    }

    private static AccountingCleanupBackupReceipt Receipt(AnnualAccrualHoldReleasePlan plan) =>
        new(1, plan.Source, "synthetic-only", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class CallbackAudit(GarageBalanceDbContext db, Action callback) : IAuditEventWriter
    {
        public AuditEvent? Add(AuditEventWriteRequest request) { callback(); return new AuditEventWriter(db).Add(request); }
    }
}
