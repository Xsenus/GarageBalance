using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.StorageTool;

public sealed record CleanupRow(Guid Id, string Hash);
public sealed record CleanupTable(string Table, IReadOnlyList<CleanupRow> Rows);
public sealed record CleanupBaseline(Guid GarageId, string MeterKind, Guid DeviceId, decimal Value, DateOnly StartsOn);
public sealed record SelectiveFinancialCleanupPlan(int FormatVersion, Guid PlanId, DateTimeOffset CreatedAtUtc,
    AccountingCleanupSource Source, IReadOnlyList<CleanupTable> Tables, IReadOnlyList<CleanupTable> MutableRows, IReadOnlyList<CleanupBaseline> Baselines,
    IReadOnlyDictionary<string, string> PreservedHashes, string Fingerprint, int? HeldAnnualYear = null);
public sealed record SelectiveFinancialCleanupResult(Guid PlanId, int RemovedRows, int PreservedBaselines,
    bool MustRemainOffline, string Fingerprint);

// Host-only tool. No web endpoint, no migrations, no hosted services or recalculation.
// Maintenance snapshots are deliberately exhaustive and bounded, unlike application list queries.
public sealed class SelectiveFinancialCleanupEngine(GarageBalanceDbContext db,
    IAccountingCleanupBackupVerifier backupVerifier, TimeProvider clock, IAuditEventWriter? auditWriter = null, Func<CancellationToken, Task<bool>>? confirmStopped = null,
    StagingExecutionPermit? stagingPermit = null)
{
    private static readonly string[] DeleteOrder =
        ["accrual_payment_allocations", "fund_operations", "supplier_accruals", "financial_operations", "accruals", "meter_readings"];
    private static readonly string[] MustBeEmpty =
        ["expense_payment_batch_operations", "expense_payment_batches", "cash_bank_transfers", "cash_bank_balance_operations",
         "staff_salary_adjustments", "fee_campaign_garages", "fee_campaigns", "access_import_created_records"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> Links = new Dictionary<string, string>
    {
        ["public.accrual_payment_allocations.AccrualId->accruals"] = "c",
        ["public.accrual_payment_allocations.CreditAccrualId->accruals"] = "r",
        ["public.accrual_payment_allocations.FinancialOperationId->financial_operations"] = "c",
        ["public.financial_operations.TargetAccrualId->accruals"] = "r",
        ["public.expense_payment_batch_operations.OperationId->financial_operations"] = "r",
        ["public.fund_operations.SourceFinancialOperationId->financial_operations"] = "r",
        ["public.supplier_accruals.SourceFinancialOperationId->financial_operations"] = "r"
    };

    public async Task<SelectiveFinancialCleanupPlan> PreviewAsync(CancellationToken token, int? heldAnnualYear = null, Guid? operationId = null)
    {
        RequirePostgres();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (heldAnnualYear.HasValue && heldAnnualYear is < 1900 or > 9999) throw Error("cleanup_annual_year_invalid");
        if (operationId == Guid.Empty) throw Error("cleanup_plan_invalid");
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '60s'; SET LOCAL lock_timeout = '5s'", token);
        var plan = await DescribeAsync(operationId ?? Guid.NewGuid(), clock.GetUtcNow(), token, heldAnnualYear);
        await transaction.RollbackAsync(token);
        return plan;
    }

    public async Task<SelectiveFinancialCleanupResult> ExecuteAsync(SelectiveFinancialCleanupPlan plan, string confirmation,
        AccountingCleanupBackupReceipt backup, AccountingCleanupExecutionIdentity identity, string reason, CancellationToken token)
    {
        RequirePostgres();
        // A host identity or boolean flag cannot authorize staging. The permit is
        // issued only by the real installed/gate/offline proof and is rechecked.
        if (db.Database.GetDbConnection().Database == "garagebalance_staging")
        {
            if (stagingPermit is null) throw Error("cleanup_service_exclusion_unverified");
            if (plan is null) throw Error("cleanup_plan_invalid");
            await stagingPermit.VerifyAsync(plan.PlanId, plan.Fingerprint, token);
        }
        await StagingTargetBinding.VerifyAsync(db, token);
        if (!identity.HasHostPrivilege || !identity.ApplicationStopped) throw Error("cleanup_permission_denied");
        if (db.Database.GetDbConnection().Database == "garagebalance_staging" && confirmStopped is null) throw Error("cleanup_host_proof_missing");
        if (string.IsNullOrWhiteSpace(identity.Operator) || identity.Operator.Length > 120 || identity.Operator.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500 || reason.Any(char.IsControl)) throw Error("cleanup_reason_required");
        if (plan is null || plan.FormatVersion != 2 || plan.PlanId == Guid.Empty || plan.CreatedAtUtc > clock.GetUtcNow() ||
            clock.GetUtcNow() - plan.CreatedAtUtc > TimeSpan.FromHours(24) || confirmation != plan.Fingerprint ||
            Fingerprint(plan) != plan.Fingerprint) throw Error("cleanup_plan_invalid");
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        var source = await AccountingCleanupSchema.ReadAsync(db, Links, token, DeleteOrder);
        if (source != plan.Source) throw Error("cleanup_source_changed");
        await backupVerifier.VerifyAsync(backup, source, clock.GetUtcNow(), token);
        if (backup.BackupCreatedAtUtc < plan.CreatedAtUtc) throw Error("cleanup_backup_predates_plan");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // Block reads and writes of all mapped tables until hold and cleanup commit together.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s'", token);
            var lockSql = "LOCK TABLE " + string.Join(',', TableNames().Select(Quote)) + " IN ACCESS EXCLUSIVE MODE";
            await db.Database.ExecuteSqlRawAsync(lockSql, token);
            await RequireNoOtherClientsAsync(token);
            var current = await DescribeAsync(plan.PlanId, plan.CreatedAtUtc, token, plan.HeldAnnualYear);
            if (current.Fingerprint != plan.Fingerprint) throw Error("cleanup_preview_stale");
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            await VerifyFundReconciliationAsync(token);

            foreach (var baseline in plan.Baselines)
            {
                var changed = await db.MeterDevices.Where(device => device.Id == baseline.DeviceId && !device.IsCanceled)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.IsAccountingBaseline, true)
                        .SetProperty(device => device.Version, Guid.NewGuid()).SetProperty(device => device.UpdatedAtUtc, clock.GetUtcNow()), token);
                if (changed != 1) throw Error("cleanup_preview_stale");
            }
            var removed = 0;
            foreach (var table in plan.Tables)
            {
                var ids = table.Rows.Select(row => row.Id).ToArray();
                // Exact allowlist validated by the manifest; never a broad DELETE/CASCADE.
                if (!DeleteOrder.Contains(table.Table, StringComparer.Ordinal)) throw Error("cleanup_plan_invalid");
                var deleteSql = $"DELETE FROM {Quote(table.Table)} WHERE \"Id\" = ANY(@ids)";
                var count = await db.Database.ExecuteSqlRawAsync(deleteSql,
                    [new Npgsql.NpgsqlParameter("ids", ids)], token);
                if (count != ids.Length) throw Error("cleanup_preview_stale");
                removed += count;
            }
            var unchangedFunds = await RowsAsync("funds", token);
            if (JsonSerializer.Serialize(unchangedFunds, Json) != JsonSerializer.Serialize(plan.MutableRows.Single(item => item.Table == "funds").Rows, Json))
                throw Error("cleanup_fund_changed_during_delete");
            await db.Funds.ExecuteUpdateAsync(setters => setters.SetProperty(fund => fund.Balance, 0m)
                .SetProperty(fund => fund.Version, Guid.NewGuid()).SetProperty(fund => fund.UpdatedAtUtc, clock.GetUtcNow()), token);
            if (await db.Funds.AnyAsync(fund => fund.Balance != 0m, token)) throw Error("cleanup_verification_failed");
            foreach (var table in plan.Tables)
                if ((await RowsAsync(table.Table, token)).Count != 0) throw Error("cleanup_verification_failed");
            var after = await PreservedHashesAsync(token);
            if (JsonSerializer.Serialize(after, Json) != JsonSerializer.Serialize(plan.PreservedHashes, Json)) throw Error("cleanup_preservation_failed");
            var baselinesAfter = await BaselinesAsync(token, plan.Baselines);
            if (JsonSerializer.Serialize(baselinesAfter, Json) != JsonSerializer.Serialize(plan.Baselines, Json)) throw Error("cleanup_baseline_changed");
            // Only the reviewed one-year policy is appended. Existing dates/settings remain intact.
            // No generators or services run inside maintenance.
            ApplicationSetting? policy = null;
            if (plan.HeldAnnualYear is { } year)
            {
                policy = new ApplicationSetting
                {
                    Key = AnnualAccrualHoldPolicy.Key(year),
                    BooleanValue = true,
                    IntegerValue = year,
                    JsonValue = JsonSerializer.Serialize(new { plan.PlanId, reason = reason.Trim(), operatorName = identity.Operator }, Json),
                    UpdatedAtUtc = clock.GetUtcNow()
                };
                db.ApplicationSettings.Add(policy);
                var epoch = await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, true, token);
                AnnualAccrualGuardEpoch.SetYear(epoch, year, true, clock.GetUtcNow());
            }
            var writer = auditWriter ?? new AuditEventWriter(db);
            if (plan.HeldAnnualYear is { } policyYear)
                writer.Add(new(null, "finance.annual_accrual_generation_held", "application_setting", AnnualAccrualHoldPolicy.Key(policyYear),
                    AnnualAccrualHoldPolicy.Message(policyYear), ActionKind: "create", Reason: reason.Trim(),
                    Metadata: new Dictionary<string, object?> { ["year"] = policyYear, ["planId"] = plan.PlanId, ["operator"] = identity.Operator }));
            foreach (var baseline in plan.Baselines)
                writer.Add(new(null, "finance.meter_baseline_preserved", "meter_device", baseline.DeviceId.ToString(),
                    "Начальное показание сохранено независимо от очищаемой рабочей истории.",
                    ActionKind: "update", Reason: reason.Trim(), RelatedGarageId: baseline.GarageId.ToString(),
                    Metadata: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.PlanId,
                        ["meterKind"] = baseline.MeterKind,
                        ["baselineValue"] = baseline.Value.ToString("0.###", CultureInfo.InvariantCulture),
                        ["baselineStartsOn"] = baseline.StartsOn,
                        ["deviceId"] = baseline.DeviceId
                    }));
            writer.Add(new(null, "finance.selective_history_cleared", "maintenance", plan.PlanId.ToString(),
                "Очищены согласованные платежи, начисления и показания; начальные значения и история приборов сохранены.",
                ActionKind: "delete", Reason: reason.Trim(), Metadata: new Dictionary<string, object?>
                {
                    ["planHash"] = plan.Fingerprint,
                    ["backupSha256"] = backup.Sha256,
                    ["operator"] = identity.Operator,
                    ["removedRows"] = removed,
                    ["preservedBaselineCount"] = plan.Baselines.Count,
                    ["mustRemainOffline"] = !plan.HeldAnnualYear.HasValue,
                    ["heldAnnualYear"] = plan.HeldAnnualYear
                }));
            var appended = new Dictionary<string, Guid[]>
            {
                ["audit_events"] = db.AuditEvents.Local.Where(item => db.Entry(item).State == EntityState.Added).Select(item => item.Id).ToArray(),
                ["application_settings"] = policy is null ? [] : [policy.Id]
            };
            await db.SaveChangesAsync(token);
            // Verify after audit/policy writes too: database triggers must not silently mutate
            // preserved history. Ignore only the exact intentional new IDs, never whole tables.
            var finalPreserved = await PreservedHashesAsync(token, appended);
            if (JsonSerializer.Serialize(finalPreserved, Json) != JsonSerializer.Serialize(plan.PreservedHashes, Json)) throw Error("cleanup_preservation_failed");
            if (policy is not null)
            {
                var storedPolicy = await db.ApplicationSettings.AsNoTracking().SingleAsync(item => item.Id == policy.Id, token);
                if (storedPolicy.Key != policy.Key || !storedPolicy.BooleanValue || storedPolicy.IntegerValue != policy.IntegerValue ||
                    !JsonNode.DeepEquals(JsonNode.Parse(storedPolicy.JsonValue ?? "null"), JsonNode.Parse(policy.JsonValue ?? "null")) || storedPolicy.DateValue != policy.DateValue || storedPolicy.Version != policy.Version ||
                    storedPolicy.UpdatedByUserId != policy.UpdatedByUserId || Math.Abs((storedPolicy.UpdatedAtUtc - policy.UpdatedAtUtc).Ticks) >= 10)
                    throw Error("cleanup_annual_policy_changed");
            }
            if (await db.Funds.AnyAsync(fund => fund.Balance != 0m, token)) throw Error("cleanup_verification_failed");
            foreach (var table in plan.Tables)
                if ((await RowsAsync(table.Table, token)).Count != 0) throw Error("cleanup_verification_failed");
            await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
            if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
            if (stagingPermit is not null) await stagingPermit.VerifyAsync(plan.PlanId, plan.Fingerprint, token);
            await RequireNoOtherClientsAsync(token);
            token.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            return new(plan.PlanId, removed, plan.Baselines.Count, !plan.HeldAnnualYear.HasValue, plan.Fingerprint);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private async Task RequireNoOtherClientsAsync(CancellationToken token)
    {
        // Statistics can otherwise retain an earlier activity snapshot in this
        // transaction, hiding a client that connected after the first check.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", token);
        var clients = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity
            WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend'
            """).SingleAsync(token);
        if (clients != 0) throw Error("cleanup_other_clients_connected");
    }

    // Absence of an audit alone cannot establish rollback. Take every mapped
    // table lock before a fresh Read Committed snapshot, then require the exact
    // original manifest. An in-flight commit must finish before this comparison.
    // This method never executes deletion or changes the policy/data.
    public async Task VerifyRolledBackAsync(SelectiveFinancialCleanupPlan plan, CancellationToken token)
    {
        RequirePostgres();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (plan is null || plan.FormatVersion != 2 || plan.PlanId == Guid.Empty || plan.HeldAnnualYear != 2026 ||
            plan.Fingerprint != Fingerprint(plan)) throw Error("cleanup_plan_invalid");
        if (db.Database.CurrentTransaction is not null) throw Error("cleanup_transaction_active");
        if (db.Database.GetDbConnection().Database == "garagebalance_staging" && confirmStopped is null)
            throw Error("cleanup_host_proof_missing");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '60s'", token);
        var lockSql = "LOCK TABLE " + string.Join(',', TableNames().Select(Quote)) + " IN ACCESS EXCLUSIVE MODE";
        await db.Database.ExecuteSqlRawAsync(lockSql, token);
        var clients = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity
            WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend'
            """).SingleAsync(token);
        if (clients != 0) throw Error("cleanup_other_clients_connected");
        if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
        var planId = plan.PlanId.ToString();
        if (await db.AuditEvents.AsNoTracking().AnyAsync(e => e.Action == "finance.selective_history_cleared" && e.EntityId == planId, token))
            throw Error("maintenance_commit_already_recorded");
        var current = await DescribeAsync(plan.PlanId, plan.CreatedAtUtc, token, plan.HeldAnnualYear);
        if (current.Fingerprint != plan.Fingerprint) throw Error("maintenance_rollback_unverified");
        if (confirmStopped is not null && !await confirmStopped(token)) throw Error("cleanup_host_started");
        await transaction.RollbackAsync(token);
    }

    // The durable receipt is written only after the full pre-start verification.
    // After startup, monthly rows may legitimately exist, so verify the receipt's
    // audit/hold basis without claiming the current tables still match the old snapshot.
    public async Task VerifyRecoveryAfterStartAsync(SelectiveFinancialCleanupPlan plan, CancellationToken token)
    {
        RequirePostgres();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (plan.HeldAnnualYear != 2026 || plan.Fingerprint != Fingerprint(plan) || db.Database.CurrentTransaction is not null)
            throw Error("cleanup_plan_invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        var id = plan.PlanId.ToString();
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.Action == "finance.selective_history_cleared" && e.EntityId == id).ToListAsync(token);
        if (events.Count != 1 || string.IsNullOrEmpty(events[0].MetadataJson)) throw Error("maintenance_commit_unverified");
        using var metadata = JsonDocument.Parse(events[0].MetadataJson!);
        if (!metadata.RootElement.TryGetProperty("planHash", out var hash) || hash.ValueKind != JsonValueKind.String || hash.GetString() != plan.Fingerprint)
            throw Error("maintenance_commit_unverified");
        var held = await db.ApplicationSettings.AsNoTracking().CountAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026) && s.BooleanValue && s.IntegerValue == 2026, token);
        if (held != 1) throw Error("cleanup_annual_policy_changed");
        await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        await transaction.RollbackAsync(token);
    }

    // Only a trusted durable rolled-back receipt permits this weaker post-start
    // check: normal monthly activity may already have changed the old snapshot.
    // The runner must first establish exact rollback under exclusive locks before
    // creating that receipt. This method never treats absent audit as rollback.
    public async Task VerifyRollbackRecoveryAfterStartAsync(SelectiveFinancialCleanupPlan plan, CancellationToken token)
    {
        RequirePostgres();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (plan.FormatVersion != 2 || plan.PlanId == Guid.Empty || plan.HeldAnnualYear != 2026 ||
            plan.Fingerprint != Fingerprint(plan) || db.Database.CurrentTransaction is not null) throw Error("cleanup_plan_invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        if (await AccountingCleanupSchema.ReadAsync(db, Links, token, DeleteOrder) != plan.Source) throw Error("cleanup_source_changed");
        var id = plan.PlanId.ToString();
        if (await db.AuditEvents.AsNoTracking().AnyAsync(e => e.Action == "finance.selective_history_cleared" && e.EntityId == id, token))
            throw Error("maintenance_commit_already_recorded");
        await transaction.RollbackAsync(token);
    }

    // Independent, fresh read-only verification after commit, before restart.
    public async Task VerifyPostCommitAsync(SelectiveFinancialCleanupPlan plan, CancellationToken token)
    {
        RequirePostgres();
        await StagingTargetBinding.VerifyAsync(db, token);
        if (plan.HeldAnnualYear != 2026 || plan.Fingerprint != Fingerprint(plan) || db.Database.CurrentTransaction is not null)
            throw Error("cleanup_plan_invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
        if (await AccountingCleanupSchema.ReadAsync(db, Links, token, DeleteOrder) != plan.Source) throw Error("cleanup_source_changed");
        foreach (var table in DeleteOrder)
            if (await CountAsync(table, token) != 0) throw Error("cleanup_verification_failed");
        if (await db.Funds.AsNoTracking().AnyAsync(fund => fund.Balance != 0m, token)) throw Error("cleanup_verification_failed");
        var policy = await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == AnnualAccrualHoldPolicy.Key(2026), token);
        if (policy is null || !policy.BooleanValue || policy.IntegerValue != 2026) throw Error("cleanup_annual_policy_changed");
        var planIdText = plan.PlanId.ToString();
        var deviceIds = plan.Baselines.Select(b => b.DeviceId.ToString()).ToArray();
        var audits = await db.AuditEvents.AsNoTracking().Where(e =>
            (e.Action == "finance.selective_history_cleared" && e.EntityId == planIdText) ||
            (e.MetadataJson != null && e.MetadataJson.Contains(planIdText) &&
                ((e.Action == "finance.annual_accrual_generation_held" && e.EntityId == "finance.annual_accrual_hold.2026") ||
                 (e.Action == "finance.meter_baseline_preserved" && e.EntityId != null && deviceIds.Contains(e.EntityId))))).ToListAsync(token);
        var matching = new List<GarageBalance.Api.Domain.Audit.AuditEvent>();
        foreach (var audit in audits)
        {
            if (string.IsNullOrEmpty(audit.MetadataJson)) continue;
            using var metadata = JsonDocument.Parse(audit.MetadataJson);
            var cleared = audit.Action == "finance.selective_history_cleared" && audit.EntityId == plan.PlanId.ToString() &&
                metadata.RootElement.TryGetProperty("planHash", out var hash) && hash.ValueKind == JsonValueKind.String && hash.GetString() == plan.Fingerprint;
            var related = audit.Action != "finance.selective_history_cleared" && metadata.RootElement.TryGetProperty("planId", out var id) &&
                id.ValueKind == JsonValueKind.String && id.GetString() == plan.PlanId.ToString();
            if (cleared || related)
                matching.Add(audit);
        }
        if (matching.Count != plan.Baselines.Count + 2 ||
            matching.Count(e => e.Action == "finance.selective_history_cleared" && e.EntityId == plan.PlanId.ToString()) != 1 ||
            matching.Count(e => e.Action == "finance.annual_accrual_generation_held" && e.EntityId == AnnualAccrualHoldPolicy.Key(2026)) != 1 ||
            plan.Baselines.Any(b => matching.Count(e => e.Action == "finance.meter_baseline_preserved" && e.EntityId == b.DeviceId.ToString()) != 1))
            throw Error("maintenance_commit_unverified");
        var baselineIds = plan.Baselines.Select(item => item.DeviceId).ToHashSet();
        var originalDevices = plan.MutableRows.Single(item => item.Table == "meter_devices").Rows;
        var currentDevices = await RowsAsync("meter_devices", token);
        if (currentDevices.Count != originalDevices.Count ||
            !currentDevices.Where(row => !baselineIds.Contains(row.Id)).SequenceEqual(originalDevices.Where(row => !baselineIds.Contains(row.Id))))
            throw Error("cleanup_preservation_failed");
        if (await db.MeterDevices.AsNoTracking().CountAsync(device => baselineIds.Contains(device.Id) && device.IsAccountingBaseline, token) != baselineIds.Count)
            throw Error("cleanup_baseline_changed");
        var appended = new Dictionary<string, Guid[]> { ["audit_events"] = matching.Select(e => e.Id).ToArray(), ["application_settings"] = [policy.Id] };
        var preserved = await PreservedHashesAsync(token, appended);
        if (JsonSerializer.Serialize(preserved, Json) != JsonSerializer.Serialize(plan.PreservedHashes, Json)) throw Error("cleanup_preservation_failed");
        if (JsonSerializer.Serialize(await BaselinesAsync(token, plan.Baselines), Json) != JsonSerializer.Serialize(plan.Baselines, Json)) throw Error("cleanup_baseline_changed");
        await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        await transaction.RollbackAsync(token);
    }

    private async Task<SelectiveFinancialCleanupPlan> DescribeAsync(Guid id, DateTimeOffset created, CancellationToken token, int? heldAnnualYear)
    {
        if (heldAnnualYear.HasValue)
        {
            if (heldAnnualYear is < 1900 or > 9999) throw Error("cleanup_annual_year_invalid");
            var key = AnnualAccrualHoldPolicy.Key(heldAnnualYear.Value);
            if (await db.ApplicationSettings.AnyAsync(setting => setting.Key == key, token)) throw Error("cleanup_annual_policy_exists");
        }
        var source = await AccountingCleanupSchema.ReadAsync(db, Links, token, DeleteOrder);
        await AnnualAccrualGuardEpoch.ReadAndValidateAsync(db, false, token);
        foreach (var table in TableNames())
            if (await CountAsync(table, token) > 250000) throw Error("cleanup_scope_too_large");
        foreach (var table in MustBeEmpty)
            if (await CountAsync(table, token) != 0)
                throw Error("cleanup_unreviewed_operations");
        if (await db.FinancialOperations.AnyAsync(operation => operation.OperationKind != FinancialOperationKinds.Income ||
            operation.GarageId == null || operation.StaffMemberId != null || operation.SupplierId != null, token))
            throw Error("cleanup_unreviewed_operations");
        // Existing physical replacement links cannot be removed as ordinary readings. A
        // future archive/history design is required for such a database; fail closed today.
        if (await db.MeterDevices.AnyAsync(device => device.ReplacementReadingId != null, token) ||
            await db.MeterReadings.AnyAsync(reading => reading.IsMeterReplacement, token)) throw Error("cleanup_replacement_reading_protected");
        await VerifyFundReconciliationAsync(token);
        var tables = new List<CleanupTable>();
        foreach (var table in DeleteOrder) tables.Add(new(table, await RowsAsync(table, token)));
        var mutable = new[] { new CleanupTable("meter_devices", await RowsAsync("meter_devices", token)),
            new CleanupTable("funds", await RowsAsync("funds", token)),
            new CleanupTable("application_settings", await RowsAsync("application_settings", token)) };
        var plan = new SelectiveFinancialCleanupPlan(2, id, created, source, tables, mutable, await BaselinesAsync(token), await PreservedHashesAsync(token), "", heldAnnualYear);
        return plan with { Fingerprint = Fingerprint(plan) };
    }

    private async Task VerifyFundReconciliationAsync(CancellationToken token)
    {
        // Covers every fund independently; canceled operations have no balance effect.
        // Reject unknown operation kinds and residuals rather than deleting an opening balance.
        var invalid = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM funds f
            LEFT JOIN (
                SELECT "FundId", sum(CASE WHEN "IsCanceled" THEN 0
                    WHEN "OperationKind" = 'deposit' THEN "Amount"
                    WHEN "OperationKind" = 'withdraw' THEN -"Amount" ELSE 0 END) AS net,
                    bool_or(NOT "IsCanceled" AND "OperationKind" NOT IN ('deposit','withdraw')) AS unknown_kind
                FROM fund_operations GROUP BY "FundId"
            ) ledger ON ledger."FundId" = f."Id"
            WHERE f."Balance" <> coalesce(ledger.net, 0) OR coalesce(ledger.unknown_kind, false)
            """).SingleAsync(token);
        if (invalid != 0) throw Error("cleanup_fund_residual");
    }

    private async Task<IReadOnlyList<CleanupBaseline>> BaselinesAsync(CancellationToken token, IReadOnlyList<CleanupBaseline>? selected = null)
    {
        var result = new List<CleanupBaseline>();
        // One host maintenance snapshot, capped by the per-table row limit below.
        var garages = await db.Garages.AsNoTracking().ToDictionaryAsync(garage => garage.Id, token);
        var devices = await db.MeterDevices.AsNoTracking().Where(device => !device.IsCanceled).ToListAsync(token);
        var readings = await db.MeterReadings.AsNoTracking().Where(reading => !reading.IsCanceled).ToListAsync(token);
        var connections = await db.GarageMeterConnections.AsNoTracking().Select(connection => new { connection.GarageId, connection.MeterKind }).ToListAsync(token);
        foreach (var group in devices.GroupBy(device => (device.GarageId, device.MeterKind)).OrderBy(group => group.Key.GarageId).ThenBy(group => group.Key.MeterKind, StringComparer.Ordinal))
        {
            if (selected is not null ? !selected.Any(item => item.GarageId == group.Key.GarageId && item.MeterKind == group.Key.MeterKind)
                : !readings.Any(reading => reading.GarageId == group.Key.GarageId && reading.MeterKind == group.Key.MeterKind)) continue;
            if (connections.Any(connection => connection.GarageId == group.Key.GarageId && connection.MeterKind == group.Key.MeterKind)) continue;
            var history = group.OrderBy(device => device.InstalledOn).ThenBy(device => device.Id).ToArray();
            var first = readings.Where(reading => reading.GarageId == group.Key.GarageId && reading.MeterKind == group.Key.MeterKind)
                .OrderBy(reading => reading.AccountingMonth).ThenBy(reading => reading.Id).FirstOrDefault();
            var baseline = MeterBaselineResolver.FindBaselineDevice(history, first);
            if (baseline is null) throw Error("cleanup_baseline_ambiguous");
            var garage = garages[group.Key.GarageId];
            result.Add(new(garage.Id, group.Key.MeterKind, baseline.Id, baseline.InitialValue,
                MeterBaselineResolver.GetAccountingStartDate(baseline, garage.AccountingStartMonth, history, first, garage.InitialMeterReadingMonth)));
        }
        // Reading-only legacy chains need an explicit reviewed opening before all readings can go.
        if (readings.Any(reading => !devices.Any(device => device.GarageId == reading.GarageId && device.MeterKind == reading.MeterKind) &&
            !connections.Any(connection => connection.GarageId == reading.GarageId && connection.MeterKind == reading.MeterKind))) throw Error("cleanup_baseline_ambiguous");
        return result;
    }

    private async Task<IReadOnlyDictionary<string, string>> PreservedHashesAsync(CancellationToken token, IReadOnlyDictionary<string, Guid[]>? appended = null)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in TableNames().Except(DeleteOrder, StringComparer.Ordinal))
        {
            var excluded = table switch
            {
                "meter_devices" => "ARRAY['IsAccountingBaseline','Version','UpdatedAtUtc']",
                "funds" => "ARRAY['Balance','Version','UpdatedAtUtc']",
                _ => "ARRAY[]::text[]"
            };
            // Only the epoch's explicitly mutable fields are excluded; all other settings stay hashed.
            var projection = table == "application_settings"
                ? "CASE WHEN t.\"Key\" = 'finance.annual_accrual_guard_epoch' THEN to_jsonb(t)-ARRAY['JsonValue','Version','UpdatedAtUtc'] ELSE to_jsonb(t) END"
                : $"to_jsonb(t)-{excluded}";
            // Hash in PostgreSQL; no personal/settings/secret values are exported in manifests.
            var newIds = appended?.GetValueOrDefault(table) ?? [];
            var where = newIds.Length == 0 ? "" : " WHERE NOT (t.\"Id\" = ANY(@appended))";
            var hashSql = $"""
                SELECT encode(sha256(convert_to(COALESCE(string_agg(h, '' ORDER BY h), ''), 'UTF8')), 'hex') AS "Value"
                FROM (SELECT encode(sha256(convert_to(({projection})::text,'UTF8')),'hex') h FROM {Quote(table)} t{where}) rows
                """;
            var hash = await db.Database.SqlQueryRaw<string>(hashSql, newIds.Length == 0 ? [] : [new Npgsql.NpgsqlParameter("appended", newIds)]).SingleAsync(token);
            hashes.Add(table, hash);
        }
        return hashes;
    }

    private async Task<IReadOnlyList<CleanupRow>> RowsAsync(string table, CancellationToken token)
    {
        var rowSql = $"""
            SELECT "Id", encode(sha256(convert_to(to_jsonb(t)::text,'UTF8')),'hex') AS "Hash"
            FROM {Quote(table)} t ORDER BY "Id" LIMIT 250001
            """;
        var rows = await db.Database.SqlQueryRaw<CleanupRow>(rowSql).ToListAsync(token);
        if (rows.Count > 250000) throw Error("cleanup_scope_too_large");
        return rows;
    }

    public static string Fingerprint(SelectiveFinancialCleanupPlan plan) =>
        AccountingCleanupSchema.Hash(JsonSerializer.Serialize(plan with { Fingerprint = "" }, Json));
    private Task<long> CountAsync(string table, CancellationToken token)
    {
        var sql = $"SELECT count(*) AS \"Value\" FROM {Quote(table)}";
        return db.Database.SqlQueryRaw<long>(sql).SingleAsync(token);
    }
    private string[] TableNames() => db.Model.GetEntityTypes().Select(entity => entity.GetTableName()!).Distinct().Order(StringComparer.Ordinal).ToArray();
    private static string Quote(string table) => "public.\"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private void RequirePostgres()
    {
        if (!db.Database.IsNpgsql()) throw Error("cleanup_postgres_required");
        var database = db.Database.GetDbConnection().Database;
        if (!Regex.IsMatch(database, "^garagebalance_(staging|it_[a-z0-9_]+)$", RegexOptions.CultureInvariant))
            throw Error("cleanup_database_not_allowed");
        if (db.ChangeTracker.HasChanges()) throw Error("cleanup_tracked_changes");
    }
    private static AccountingCleanupException Error(string code) => new(code, "Результат операции не подтверждён. Проверьте точный план, аудит, сохранность данных и состояние обслуживания перед повторным запуском.");
}
