using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed partial class ProtocolFixture
{
    private const string AnnualAction = "finance.annual_accrual_generation_resumed";
    private string AnnualPlanPath => Path.Combine(root, "annual-plan.json");
    private string AnnualSnapshotPath => Path.Combine(root, "annual-preserved.json");
    private string AnnualCallsPath => Path.Combine(root, "annual-release-calls");

    private async Task<int> RunAnnualAsync(string command, string? fault)
    {
        switch (command)
        {
            case "annual-seed": await SeedAnnualAsync(); break;
            case "annual-preview":
                await using (var db = Context())
                {
                    var plan = await AnnualEngine(db).PreviewAsync(2026, default);
                    Assert.NotNull(plan.Forecast);
                    Assert.True(plan.Forecast.HoldActive && plan.Forecast.IsComplete);
                    Assert.Equal(2, plan.Forecast.NewAccrualCount);
                    Assert.Equal(2400m, plan.Forecast.NewAccrualAmount);
                    await WriteAsync(AnnualPlanPath, plan, default);
                }
                break;
            case "annual-backup": await BackupAnnualAsync(); break;
            case "annual-execute": await ExecuteAnnualAsync(fault); break;
            case "annual-recover": await RecoverAnnualAsync(); break;
            case "annual-corrupt-outcome":
                // Synthetic fault only. The constructor binds us to the private Unix socket/database.
                await using (var db = Context())
                {
                    var hold = await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_hold.2026");
                    Assert.False(hold.BooleanValue);
                    hold.Version = Guid.NewGuid();
                    await db.SaveChangesAsync();
                }
                break;
            case "annual-assert": await AssertAnnualAsync(fault ?? throw new InvalidOperationException("fixture_outcome_missing")); break;
            default: return 64;
        }
        Console.WriteLine("fixtureCommand=" + command + ":ok");
        return 0;
    }

    private static AnnualAccrualHoldReleaseEngine AnnualEngine(GarageBalanceDbContext db) =>
        new(db, new AccountingCleanupBackupVerifier(), TimeProvider.System, businessDate: new AnnualFixtureBusinessDate());

    private async Task SeedAnnualAsync()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var seed = await FixtureData.SeedAsync(db);
        await FixtureData.SeedFinancialHistoryAsync(db, seed);
        db.AddRange(new ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = true },
            new ApplicationSetting { Key = "finance.annual_accrual_hold.2028", BooleanValue = true });
        (await db.ApplicationSettings.SingleAsync(s => s.Key == AnnualAccrualGuardEpoch.Key)).JsonValue = "{\"2026\":true,\"2028\":true}";
        var income = new IncomeType { Name = "Synthetic annual release income" };
        var tariff = new Tariff { Name = "Synthetic annual release tariff", CalculationBase = TariffCalculationBases.Fixed, Rate = 1200, EffectiveFrom = new(2026, 1, 1) };
        db.Add(new ChargeServiceSetting
        {
            Name = "Synthetic annual release service", IncomeType = income, Tariff = tariff,
            IsRegular = true, PeriodicityMonths = 12, AccrualStartMonth = 8, PaymentDueDay = 20,
            UnitName = TariffCalculationBases.GetUnitName(TariffCalculationBases.Fixed)
        });
        await db.SaveChangesAsync();
        await WriteAsync(AnnualSnapshotPath, await PreservedAnnualRowsAsync(db), default);
    }

    private static async Task<SortedDictionary<string, string>> PreservedAnnualRowsAsync(GarageBalanceDbContext db)
    {
        // Financial data must not change when releasing a hold. Compare complete rows,
        // not counts alone, including dictionaries/baselines/allocations/other holds.
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var identifiers = new NpgsqlCommandBuilder();
        foreach (var table in new[] { "garages", "meter_devices", "meter_readings", "accruals", "financial_operations",
                     "accrual_payment_allocations", "funds", "fund_operations", "supplier_accruals", "charge_service_settings", "tariffs", "income_types" })
        {
            // Identifiers cannot be SQL parameters. Use a fixed allowlist and the
            // provider's identifier quoting, never interpolation of raw names.
            var sql = "SELECT to_jsonb(t)::text AS \"Value\" FROM " + identifiers.QuoteIdentifier(table) +
                " AS t ORDER BY to_jsonb(t)::text COLLATE \"C\"";
            var rows = await db.Database.SqlQueryRaw<string>(sql).ToListAsync();
            snapshot[table] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
        }
        var otherHolds = await db.ApplicationSettings.AsNoTracking().Where(s => s.Key == "finance.annual_accrual_hold.2028").ToArrayAsync();
        snapshot["other-held-years"] = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(otherHolds, Json)));
        return snapshot;
    }

    private async Task BackupAnnualAsync()
    {
        var plan = await ReadAsync<AnnualAccrualHoldReleasePlan>(AnnualPlanPath);
        var settings = new NpgsqlConnectionStringBuilder(connection);
        var path = Path.Combine(root, "annual-pre-release.pgdump");
        var created = DateTimeOffset.UtcNow;
        await NativeAsync("pg_dump", ["--format=custom", "--file=" + path, "--host=" + settings.Host, "--port=55483", "--username=postgres", "--dbname=" + settings.Database]);
        var restoreName = "garagebalance_it_restore_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using (var create = new NpgsqlCommand("CREATE DATABASE \"" + restoreName + "\"", admin)) await create.ExecuteNonQueryAsync();
            try
            {
                await NativeAsync("pg_restore", ["--exit-on-error", "--host=" + settings.Host, "--port=55483", "--username=postgres", "--dbname=" + restoreName, path]);
                await using var restored = Context(new NpgsqlConnectionStringBuilder(connection) { Database = restoreName }.ConnectionString);
                var copy = await AnnualEngine(restored).PreviewAsync(plan.Year, default);
                Assert.Equal(plan.Source.SchemaFingerprint, copy.Source.SchemaFingerprint);
                Assert.Equal(plan.SettingHash, copy.SettingHash);
                Assert.Equal(plan.EpochHash, copy.EpochHash);
                Assert.Equal(await File.ReadAllTextAsync(AnnualSnapshotPath), JsonSerializer.Serialize(await PreservedAnnualRowsAsync(restored), Json));
            }
            finally
            {
                await using var drop = new NpgsqlCommand("DROP DATABASE \"" + restoreName + "\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
        await using var stream = File.OpenRead(path);
        await WriteAsync(ReceiptPath, new AccountingCleanupBackupReceipt(1, plan.Source, path,
            Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)), created, DateTimeOffset.UtcNow,
            plan.Source.SchemaFingerprint, true), default);
        Console.WriteLine("fixtureAnnualBackup=restored_and_verified");
    }

    private async Task ExecuteAnnualAsync(string? fault)
    {
        var plan = await ReadAsync<AnnualAccrualHoldReleasePlan>(AnnualPlanPath);
        var backup = await ReadAsync<AccountingCleanupBackupReceipt>(ReceiptPath);
        var lifecycle = Lifecycle();
        using var host = Host(plan.PlanId, plan.Fingerprint, lifecycle, () => lifecycle.ServiceWasActive, () => lifecycle.ParticipantState,
            async token =>
            {
                if (fault == "outcome-read") throw new IOException("fixture_outcome_read_failed");
                await using var db = Context();
                Assert.Equal(AnnualAccrualReleaseOutcome.Committed, await AnnualEngine(db).VerifyOutcomeAsync(plan, token));
            }, async token =>
            {
                if (fault == "after-receipt") await SignalAsync(fault, token);
                await lifecycle.RestoreAsync(token);
                if (fault == "after-start") throw new IOException("fixture_health_completion_failed");
            });
        var decorated = new AnnualFaultHost(host, fault);
        await AnnualAccrualReleaseProtocol.ExecuteAsync(decorated, async (phase, token) =>
        {
            if (phase == "verify-fresh") { await host.RequireFreshAnnualReleaseAsync(token); return; }
            await host.TransitionPreparationAsync(phase, phase == "preparing" ? null : plan.Fingerprint, token);
            if (phase == "executing")
            {
                if (fault == "lost-lock") await decorated.DropActivityGateAsync();
                host.VerifyHeldProtocol(plan.PlanId, plan.Fingerprint);
                // A fixture must never acquire authority to touch garagebalance_staging.
                Assert.Throws<InvalidOperationException>(() => host.VerifyProductionExecutionAuthority(plan.PlanId, plan.Fingerprint));
            }
        }, async token =>
        {
            host.VerifyHeldProtocol(plan.PlanId, plan.Fingerprint);
            await File.AppendAllTextAsync(AnnualCallsPath, plan.PlanId + "\n", token);
            if (fault == "before-release") await SignalAsync(fault, token);
            await using var db = Context();
            var offlineProofs = 0;
            var engine = new AnnualAccrualHoldReleaseEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                auditWriter: fault == "before-commit" ? new PausingAudit(db, () => SignalAsync(fault, default).GetAwaiter().GetResult()) : null,
                confirmStopped: async ct =>
                {
                    if (++offlineProofs == 2 && fault == "lost-lock-before-commit") await decorated.DropActivityGateAsync();
                    host.VerifyHeldProtocol(plan.PlanId, plan.Fingerprint);
                    await lifecycle.VerifyOfflineAsync(ct);
                    return true;
                },
                businessDate: new AnnualFixtureBusinessDate());
            await engine.ReleaseAsync(plan, plan.Fingerprint, backup, new(true, true, "isolated-annual-fixture"), "Synthetic reviewed annual release", token);
            if (fault == "after-commit") await SignalAsync(fault, token);
        }, default);
    }

    private async Task RecoverAnnualAsync()
    {
        var plan = await ReadAsync<AnnualAccrualHoldReleasePlan>(AnnualPlanPath);
        var callsBefore = File.Exists(AnnualCallsPath) ? await File.ReadAllTextAsync(AnnualCallsPath) : "";
        var lifecycle = Lifecycle();
        MaintenanceRecoveryState? original = null;
        using var host = Host(plan.PlanId, plan.Fingerprint, lifecycle, () => original!.ServiceWasActive, () => original!.Participants);
        await using var db = Context();
        var auditsBefore = await db.AuditEvents.CountAsync(e => e.Action == AnnualAction);
        var result = await StagingAnnualAccrualReleaseRecovery.ResumeAsync(host, lifecycle, db, plan, state => original = state, default);
        Assert.Equal(callsBefore, File.Exists(AnnualCallsPath) ? await File.ReadAllTextAsync(AnnualCallsPath) : "");
        Assert.Equal(auditsBefore, await db.AuditEvents.CountAsync(e => e.Action == AnnualAction));
        Console.WriteLine("fixtureAnnualRecovery=" + result + ":without_release_callback");
    }

    private async Task AssertAnnualAsync(string outcome)
    {
        var blocked = outcome == "blocked";
        if (blocked) await OfflineAsync(default);
        else await HealthAsync(default);
        await using var db = Context();
        Assert.Equal(await File.ReadAllTextAsync(AnnualSnapshotPath), JsonSerializer.Serialize(await PreservedAnnualRowsAsync(db), Json));
        Assert.Equal(0, await db.AuditEvents.CountAsync(e => e.Action == "finance.selective_history_cleared"));
        var audits = await db.AuditEvents.CountAsync(e => e.Action == AnnualAction);
        Assert.InRange(audits, 0, 1);
        if (!blocked)
        {
            var committed = outcome == "committed";
            Assert.True(committed || outcome == "rolled-back");
            Assert.Equal(committed ? 1 : 0, audits);
            var hold = await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == "finance.annual_accrual_hold.2026");
            var epoch = await db.ApplicationSettings.AsNoTracking().SingleAsync(s => s.Key == AnnualAccrualGuardEpoch.Key);
            Assert.Equal(!committed, hold.BooleanValue);
            var plan = await ReadAsync<AnnualAccrualHoldReleasePlan>(AnnualPlanPath);
            // The production verifier already ran while offline. Do not call its
            // no-other-clients gate after intentionally restoring the real API/jobs.
            var compactJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var holdHash = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(hold, compactJson));
            var epochHash = AccountingCleanupSchema.Hash(JsonSerializer.Serialize(epoch, compactJson));
            if (committed)
            {
                var audit = await db.AuditEvents.AsNoTracking().SingleAsync(e => e.Action == AnnualAction);
                using var metadata = JsonDocument.Parse(audit.MetadataJson!);
                Assert.Equal(plan.PlanId.ToString(), metadata.RootElement.GetProperty("planId").GetString());
                Assert.Equal(plan.Fingerprint, metadata.RootElement.GetProperty("planHash").GetString());
                Assert.Equal(holdHash, metadata.RootElement.GetProperty("settingAfterHash").GetString());
                Assert.Equal(epochHash, metadata.RootElement.GetProperty("epochAfterHash").GetString());
            }
            else { Assert.Equal(plan.SettingHash, holdHash); Assert.Equal(plan.EpochHash, epochHash); }
        }
        foreach (var name in new[] { "maintenance-active", "recovery-required", "participants-resume-required", "operation-progress" })
            if (!blocked) Assert.False(File.Exists(Path.Combine(barrierDirectory, name)), "fixture_journal_not_removed:" + name);
        if (blocked) Assert.True(File.Exists(Path.Combine(barrierDirectory, "maintenance-active")), "fixture_unknown_outcome_not_blocked");
        foreach (var timer in new[] { "garagebalance-healthcheck.timer", "garagebalance-storage-sync.timer" })
        {
            var state = await SystemctlAsync(["show", timer, "--property=ActiveState", "--all"], default);
            Assert.Contains("ActiveState=" + (blocked ? "inactive" : "active"), state.Output);
        }
    }

    private sealed class AnnualFixtureBusinessDate : IBusinessDateProvider
    {
        public DateOnly SystemDate => new(2026, 11, 9);
        public DateOnly Today => SystemDate;
        public DateOnly? OverrideDate => null;
        public void SetOverride(DateOnly? value) => throw new InvalidOperationException("fixture_date_is_fixed");
    }

    // Faults decorate only this private fixture host. Production files and authority
    // factories are unmodified, and every successful operation delegates to the real host.
    private sealed class AnnualFaultHost(LinuxMaintenanceBarrierHost inner, string? fault) : IMaintenanceBarrierHost
    {
        private IAsyncDisposable? activity;
        public ValueTask<IAsyncDisposable> AcquireDeployGateAsync(CancellationToken token) => inner.AcquireDeployGateAsync(token);
        public Task CaptureServiceStateAsync(CancellationToken token) => inner.CaptureServiceStateAsync(token);
        public async Task CreateDurableMarkerAsync(CancellationToken token)
        {
            await inner.CreateDurableMarkerAsync(token);
            if (fault == "marker-create") throw new IOException("fixture_marker_write_failed");
        }
        public Task StopAndDrainAsync(CancellationToken token) => inner.StopAndDrainAsync(token);
        public async ValueTask<IAsyncDisposable> AcquireActivityGateAsync(CancellationToken token)
        {
            activity = await inner.AcquireActivityGateAsync(token);
            return new Lease(this);
        }
        public async ValueTask DropActivityGateAsync()
        {
            if (activity is not null) { await activity.DisposeAsync(); activity = null; }
        }
        public Task VerifyOfflineAsync(CancellationToken token) => inner.VerifyOfflineAsync(token);
        public Task VerifyCommittedAsync(CancellationToken token) => inner.VerifyCommittedAsync(token);
        public Task CreateRecoveryReceiptAsync(CancellationToken token)
        {
            if (fault == "receipt-create") throw new IOException("fixture_receipt_write_failed");
            return inner.CreateRecoveryReceiptAsync(token);
        }
        public Task RemoveDurableMarkerAsync(CancellationToken token)
        {
            if (fault == "marker-remove") throw new IOException("fixture_marker_remove_failed");
            return inner.RemoveDurableMarkerAsync(token);
        }
        public Task RestoreServiceAsync(CancellationToken token) => inner.RestoreServiceAsync(token);
        public Task PermitParticipantsAsync(CancellationToken token) => inner.PermitParticipantsAsync(token);
        public Task RestoreParticipantsAsync(CancellationToken token) => inner.RestoreParticipantsAsync(token);
        public Task CompleteRecoveryAsync(CancellationToken token) => inner.CompleteRecoveryAsync(token);
        private sealed class Lease(AnnualFaultHost host) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => host.DropActivityGateAsync();
        }
    }
}
