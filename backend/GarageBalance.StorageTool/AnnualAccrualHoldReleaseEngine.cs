using System.Data;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageBalance.StorageTool;

public sealed record AnnualAccrualHoldReleasePlan(Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, int Year, Guid SettingId, Guid SettingVersion, string SettingHash, string EpochHash, string Fingerprint,
    int FormatVersion = 0, string? OperationKind = null, AnnualAccrualForecast? Forecast = null);

public enum AnnualAccrualReleaseOutcome { RolledBack, Committed }

// Explicit host-only release for one reviewed year; never expires a hold or generates charges.
public sealed class AnnualAccrualHoldReleaseEngine(GarageBalanceDbContext db,
    IAccountingCleanupBackupVerifier backupVerifier, TimeProvider clock, IAuditEventWriter? auditWriter = null,
    Func<CancellationToken, Task<bool>>? confirmStopped = null, AnnualAccrualReleasePermit? stagingPermit = null,
    IBusinessDateProvider? businessDate = null)
{
    public const string Operation = "annual-accrual-release";
    public const string AuditAction = "finance.annual_accrual_generation_resumed";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> NoDeletedTables = new Dictionary<string, string>();
    private readonly IBusinessDateProvider businessDate = businessDate ?? new BusinessDateProvider(clock, Options.Create(new RegularAccrualAutomationOptions()));

    public async Task<AnnualAccrualHoldReleasePlan> PreviewAsync(int year, CancellationToken token)
    {
        RequireSource();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        await SetTimeoutsAsync(token);
        var plan = await DescribeAsync(Guid.NewGuid(), clock.GetUtcNow(), year, token);
        await transaction.RollbackAsync(token);
        return plan;
    }

    public async Task ReleaseAsync(AnnualAccrualHoldReleasePlan plan, string confirmation,
        AccountingCleanupBackupReceipt backup, AccountingCleanupExecutionIdentity identity, string reason, CancellationToken token)
    {
        RequireSource();
        if (db.Database.GetDbConnection().Database == "garagebalance_staging")
        {
            if (stagingPermit is null) throw Error("cleanup_service_exclusion_unverified");
            await stagingPermit.VerifyAsync(plan, token);
        }
        await StagingTargetBinding.VerifyAsync(db, token);
        if (!identity.HasHostPrivilege || !identity.ApplicationStopped) throw Error("cleanup_permission_denied");
        if (db.Database.GetDbConnection().Database == "garagebalance_staging" && confirmStopped is null) throw Error("cleanup_host_proof_missing");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(identity.Operator) || identity.Operator.Length > 120 || identity.Operator.Any(char.IsControl)) throw Error("cleanup_reason_required");
        ValidatePlan(plan);
        if (plan.CreatedAtUtc > clock.GetUtcNow() || clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) ||
            confirmation != plan.Fingerprint) throw Error("cleanup_plan_invalid");
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        var source = await AccountingCleanupSchema.ReadAsync(db, NoDeletedTables, token);
        if (source != plan.Source) throw Error("cleanup_source_changed");
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), token);
        if (backup.BackupCreatedAtUtc < plan.CreatedAtUtc) throw Error("cleanup_backup_predates_plan");
        // Take the gate before the first snapshot. The forecast reads all its inputs
        // in this same serializable transaction, never through a second pool/lease.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='60s'; LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE", token);
            // Freeze every mapped input, including newly added forecast dependencies,
            // until commit. SHARE permits inspection but excludes dictionary/financial
            // writers. Table names are model metadata, never operator input.
            var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>()
                .Where(name => name != "application_settings").Distinct().Order(StringComparer.Ordinal);
            var inputLocks = "LOCK TABLE " + string.Join(',', tables.Select(name => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + " IN SHARE MODE";
            await db.Database.ExecuteSqlRawAsync(inputLocks, token);
            await RequireNoOtherClientsAsync(token);
            var current = await DescribeAsync(plan.PlanId, plan.CreatedAtUtc, plan.Year, token);
            if (StableForecast(current.Forecast!) != StableForecast(plan.Forecast!) ||
                Fingerprint(current with { Forecast = plan.Forecast }) != plan.Fingerprint) throw Error("cleanup_preview_stale");
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, true, token);
            AnnualAccrualGuardEpoch.SetYear(epoch, plan.Year, false, clock.GetUtcNow());
            var setting = await db.ApplicationSettings.SingleAsync(item => item.Id == plan.SettingId && item.Version == plan.SettingVersion, token);
            setting.BooleanValue = false;
            setting.Version = Guid.NewGuid();
            setting.UpdatedAtUtc = clock.GetUtcNow();
            // Read the database representation (jsonb/time precision) before hashing.
            // Both saves remain inside the same transaction; audit failure rolls back all.
            await db.SaveChangesAsync(token);
            var storedSetting = await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Id == plan.SettingId, token);
            var storedEpoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
            (auditWriter ?? new AuditEventWriter(db)).Add(new(null, AuditAction, "application_setting", setting.Key,
                $"Годовые начисления за {plan.Year} год разрешены отдельным решением владельца.", ActionKind: "update", Reason: reason.Trim(),
                Metadata: new Dictionary<string, object?>
                {
                    ["year"] = plan.Year,
                    ["planId"] = plan.PlanId,
                    ["operator"] = identity.Operator,
                    ["planHash"] = plan.Fingerprint,
                    ["operationKind"] = Operation,
                    ["settingBeforeHash"] = plan.SettingHash,
                    ["epochBeforeHash"] = plan.EpochHash,
                    ["settingAfterHash"] = Hash(storedSetting),
                    ["epochAfterHash"] = Hash(storedEpoch),
                    ["forecastInputFingerprint"] = plan.Forecast!.InputFingerprint
                }));
            await db.SaveChangesAsync(token);
            if (await ReadOutcomeAsync(plan, token) != AnnualAccrualReleaseOutcome.Committed) throw Error("cleanup_release_outcome_unverified");
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            if (stagingPermit is not null) await stagingPermit.VerifyAsync(plan, token);
            await RequireNoOtherClientsAsync(token);
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

    // Outcome inspection has no release callback and performs no DML. SHARE locks
    // wait out an in-flight release before fresh Read Committed reads. A transaction
    // marked READ ONLY cannot take these locks, so only the statements are read-only.
    // Missing audit alone never establishes rollback: exact original state must match.
    public async Task<AnnualAccrualReleaseOutcome> VerifyOutcomeAsync(AnnualAccrualHoldReleasePlan plan, CancellationToken token)
    {
        RequireSource();
        ValidatePlan(plan);
        await StagingTargetBinding.VerifyAsync(db, token);
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        if (db.Database.GetDbConnection().Database == "garagebalance_staging" && confirmStopped is null)
            throw Error("cleanup_host_proof_missing");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await SetTimeoutsAsync(token);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings, audit_events IN SHARE MODE", token);
        await RequireNoOtherClientsAsync(token);
        if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
        var outcome = await ReadOutcomeAsync(plan, token);
        if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
        await RequireNoOtherClientsAsync(token);
        await transaction.RollbackAsync(token);
        return outcome;
    }

    private async Task<AnnualAccrualReleaseOutcome> ReadOutcomeAsync(AnnualAccrualHoldReleasePlan plan, CancellationToken token)
    {
        if (await AccountingCleanupSchema.ReadAsync(db, NoDeletedTables, token) != plan.Source) throw Error("cleanup_source_changed");
        var key = AnnualAccrualHoldPolicy.Key(plan.Year);
        var setting = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == plan.SettingId, token);
        var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        var id = plan.PlanId.ToString();
        // Metadata is stored as text. Bound the server-side candidate search, then
        // require exact JSON identity below; malformed/ambiguous matches fail closed.
        var audits = await db.AuditEvents.AsNoTracking().Where(e => e.Action == AuditAction &&
            e.MetadataJson != null && e.MetadataJson.Contains(id)).Take(2).ToListAsync(token);
        if (setting is null || setting.Key != key || audits.Count > 1) throw Error("cleanup_release_outcome_unverified");
        if (audits.Count == 0)
        {
            if (!setting.BooleanValue || Hash(setting) != plan.SettingHash || Hash(epoch) != plan.EpochHash)
                throw Error("cleanup_release_outcome_unverified");
            return AnnualAccrualReleaseOutcome.RolledBack;
        }
        var audit = audits[0];
        using var json = JsonDocument.Parse(audit.MetadataJson!);
        var metadata = json.RootElement;
        string? Value(string name) => metadata.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (audit.EntityType != "application_setting" || audit.EntityId != key || audit.ActionKind != "update" ||
            setting.BooleanValue || setting.Version == plan.SettingVersion || Value("operationKind") != Operation || Value("planId") != id ||
            Value("planHash") != plan.Fingerprint || Value("year") != plan.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            Value("settingBeforeHash") != plan.SettingHash || Value("epochBeforeHash") != plan.EpochHash ||
            Value("settingAfterHash") != Hash(setting) || Value("epochAfterHash") != Hash(epoch) ||
            Value("forecastInputFingerprint") != plan.Forecast!.InputFingerprint)
            throw Error("cleanup_release_outcome_unverified");
        return AnnualAccrualReleaseOutcome.Committed;
    }

    private async Task RequireNoOtherClientsAsync(CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", token);
        var clients = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity
            WHERE datname=current_database() AND pid<>pg_backend_pid()
              AND (backend_type='client backend' OR backend_type IS NULL)
            """).SingleAsync(token);
        if (clients != 0) throw Error("cleanup_other_clients_connected");
    }

    private Task SetTimeoutsAsync(CancellationToken token) => db.Database.ExecuteSqlRawAsync(
        "SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='60s'", token);

    public static void ValidatePlan(AnnualAccrualHoldReleasePlan plan)
    {
        if (plan is null || plan.FormatVersion != 2 || plan.OperationKind != Operation || plan.PlanId == Guid.Empty ||
            plan.Year is < 1900 or > 9998 || plan.Source is null || plan.SettingId == Guid.Empty || plan.SettingVersion == Guid.Empty ||
            !IsHash(plan.SettingHash) || !IsHash(plan.EpochHash) || !IsHash(plan.Fingerprint) || plan.Fingerprint != Fingerprint(plan) ||
            plan.Forecast is not { IsComplete: true, HoldActive: true } forecast || forecast.Year != plan.Year ||
            forecast.Source != plan.Source || !IsHash(forecast.InputFingerprint) || forecast.Uncertainties is not { Count: 0 })
            throw Error("cleanup_plan_invalid");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static string Hash(ApplicationSetting setting) => AccountingCleanupSchema.Hash(JsonSerializer.Serialize(setting, Json));
    private static string StableForecast(AnnualAccrualForecast forecast) => JsonSerializer.Serialize(
        forecast with { GeneratedAtUtc = default, Fingerprint = "" }, Json);

    private async Task<AnnualAccrualHoldReleasePlan> DescribeAsync(Guid id, DateTimeOffset created, int year, CancellationToken token)
    {
        if (year is < 1900 or > 9998) throw Error("cleanup_annual_year_invalid");
        var key = AnnualAccrualHoldPolicy.Key(year);
        var setting = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Key == key, token);
        if (setting?.BooleanValue != true) throw Error("annual_hold_not_active");
        var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        var source = await AccountingCleanupSchema.ReadAsync(db, NoDeletedTables, token);
        var forecast = await new AnnualAccrualForecastEngine(db, businessDate, clock).PreviewAsync(year, token);
        if (!forecast.IsComplete || !forecast.HoldActive || forecast.Source != source || forecast.Uncertainties.Count != 0)
            throw Error("cleanup_annual_forecast_incomplete");
        var plan = new AnnualAccrualHoldReleasePlan(id, created, source, year, setting.Id, setting.Version,
            Hash(setting), Hash(epoch), "", 2, Operation, forecast);
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
