using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

public sealed record AnnualAccrualHoldReleasePlan(Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, int Year, Guid SettingId, Guid SettingVersion, string SettingHash, string EpochHash, string Fingerprint);

// Explicit host-only release for one reviewed year; never expires a hold or generates charges.
public sealed class AnnualAccrualHoldReleaseEngine(GarageBalanceDbContext db,
    IAccountingCleanupBackupVerifier backupVerifier, TimeProvider clock, IAuditEventWriter? auditWriter = null, Func<CancellationToken, Task<bool>>? confirmStopped = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> NoDeletedTables = new Dictionary<string, string>();

    public async Task<AnnualAccrualHoldReleasePlan> PreviewAsync(int year, CancellationToken token)
    {
        RequireSource();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        var plan = await DescribeAsync(Guid.NewGuid(), clock.GetUtcNow(), year, token);
        await transaction.RollbackAsync(token);
        return plan;
    }

    public async Task ReleaseAsync(AnnualAccrualHoldReleasePlan plan, string confirmation,
        AccountingCleanupBackupReceipt backup, AccountingCleanupExecutionIdentity identity, string reason, CancellationToken token)
    {
        RequireSource();
        if (db.Database.GetDbConnection().Database == "garagebalance_staging") throw Error("cleanup_service_exclusion_unverified");
        await StagingTargetBinding.VerifyAsync(db, token);
        if (!identity.HasHostPrivilege || !identity.ApplicationStopped) throw Error("cleanup_permission_denied");
        if (db.Database.GetDbConnection().Database == "garagebalance_staging" && confirmStopped is null) throw Error("cleanup_host_proof_missing");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(identity.Operator) || identity.Operator.Length > 120 || identity.Operator.Any(char.IsControl)) throw Error("cleanup_reason_required");
        if (plan is null || plan.Year is < 1900 or > 9999 || plan.PlanId == Guid.Empty ||
            plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            confirmation != plan.Fingerprint || Fingerprint(plan) != plan.Fingerprint) throw Error("cleanup_plan_invalid");
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        var source = await AccountingCleanupSchema.ReadAsync(db, NoDeletedTables, token);
        if (source != plan.Source) throw Error("cleanup_source_changed");
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), token);
        if (backup.BackupCreatedAtUtc < plan.CreatedAtUtc) throw Error("cleanup_backup_predates_plan");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='60s'; LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE", token);
            var clients = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend'
                """).SingleAsync(token);
            if (clients != 0) throw Error("cleanup_other_clients_connected");
            var current = await DescribeAsync(plan.PlanId, plan.CreatedAtUtc, plan.Year, token);
            if (current.Fingerprint != plan.Fingerprint) throw Error("cleanup_preview_stale");
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, true, token);
            AnnualAccrualGuardEpoch.SetYear(epoch, plan.Year, false, clock.GetUtcNow());
            var setting = await db.ApplicationSettings.SingleAsync(item => item.Id == plan.SettingId && item.Version == plan.SettingVersion, token);
            setting.BooleanValue = false;
            setting.Version = Guid.NewGuid();
            setting.UpdatedAtUtc = clock.GetUtcNow();
            (auditWriter ?? new AuditEventWriter(db)).Add(new(null, "finance.annual_accrual_generation_resumed", "application_setting", setting.Key,
                $"Годовые начисления за {plan.Year} год разрешены отдельным решением владельца.", ActionKind: "update", Reason: reason.Trim(),
                Metadata: new Dictionary<string, object?> { ["year"] = plan.Year, ["planId"] = plan.PlanId, ["operator"] = identity.Operator, ["planHash"] = plan.Fingerprint }));
            await db.SaveChangesAsync(token);
            await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            token.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private async Task<AnnualAccrualHoldReleasePlan> DescribeAsync(Guid id, DateTimeOffset created, int year, CancellationToken token)
    {
        if (year is < 1900 or > 9999) throw Error("cleanup_annual_year_invalid");
        var key = AnnualAccrualHoldPolicy.Key(year);
        var setting = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, token);
        if (setting?.BooleanValue != true) throw Error("annual_hold_not_active");
        var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        var source = await AccountingCleanupSchema.ReadAsync(db, NoDeletedTables, token);
        var plan = new AnnualAccrualHoldReleasePlan(id, created, source, year, setting.Id, setting.Version,
            AccountingCleanupSchema.Hash(JsonSerializer.Serialize(setting, Json)),
            AccountingCleanupSchema.Hash(JsonSerializer.Serialize(epoch, Json)), "");
        return plan with { Fingerprint = Fingerprint(plan) };
    }

    public static string Fingerprint(AnnualAccrualHoldReleasePlan plan) =>
        AccountingCleanupSchema.Hash(JsonSerializer.Serialize(plan with { Fingerprint = "" }, Json));

    private void RequireSource()
    {
        if (!db.Database.IsNpgsql()) throw Error("cleanup_postgresql_required");
        if (db.ChangeTracker.HasChanges()) throw Error("cleanup_tracked_changes");
        var name = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;
        if (name != "garagebalance_staging" && name?.StartsWith("garagebalance_it_", StringComparison.Ordinal) != true) throw Error("cleanup_database_not_allowed");
    }

    private static AccountingCleanupException Error(string code) => new(code, "Операция с годовым периодом остановлена: " + code);
}
