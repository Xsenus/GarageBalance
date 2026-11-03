using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using GarageBalance.Api.Tests.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace GarageBalance.Api.Tests.Maintenance;

public sealed class SelectiveFinancialCleanupTests
{
    private static readonly AccountingCleanupExecutionIdentity Identity = new(true, true, "synthetic-operator");

    [PostgreSqlFact]
    public async Task PostgreSql_MaskedLateClientBlocksCommitAndRollbackProofForUnprivilegedRole()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        await db.Database.OpenConnectionAsync();
        var role = "gb_cleanup_stats_" + Guid.NewGuid().ToString("N");
        var quoted = new Npgsql.NpgsqlCommandBuilder().QuoteIdentifier(role);
        async Task Command(string sql)
        {
            await using var command = new Npgsql.NpgsqlCommand(sql, (Npgsql.NpgsqlConnection)db.Database.GetDbConnection());
            await command.ExecuteNonQueryAsync();
        }
        await Command("CREATE ROLE " + quoted + " NOLOGIN");
        await using var concurrent = new Npgsql.NpgsqlConnection(new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        try
        {
            await Command("GRANT USAGE ON SCHEMA public TO " + quoted);
            await Command("GRANT ALL ON ALL TABLES IN SCHEMA public TO " + quoted);
            await Command("GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO " + quoted);
            await Command("SET ROLE " + quoted);
            Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT pg_has_role(current_user,'pg_read_all_stats','USAGE') AS \"Value\"").SingleAsync());
            var writer = new ConnectOnAudit(db, concurrent);
            var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System, writer);
            var plan = await engine.PreviewAsync(default, 2026);
            var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic masked late-client check", default));
            Assert.Equal("cleanup_other_clients_connected", error.Code);
            Assert.True(writer.Connected);
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE pid={concurrent.ProcessID} AND backend_type IS NULL").SingleAsync());
            Assert.Equal("cleanup_other_clients_connected", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyRolledBackAsync(plan, default))).Code);
            await concurrent.CloseAsync();
            await engine.VerifyRolledBackAsync(plan, default);
            Assert.Equal(6, await db.MeterReadings.CountAsync());
            Assert.False(await db.ApplicationSettings.AnyAsync(s => s.Key == "finance.annual_accrual_hold.2026"));
            Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "finance.selective_history_cleared"));
        }
        finally
        {
            await concurrent.CloseAsync();
            await Command("RESET ROLE");
            await Command("DROP OWNED BY " + quoted);
            await Command("DROP ROLE " + quoted);
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSql_NewClientAfterDeletionIsDetectedBeforeCommitAndEverythingRollsBack()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        await using var concurrent = new Npgsql.NpgsqlConnection(database.ConnectionString);
        var writer = new ConnectOnAudit(db, concurrent);
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System, writer);
        var plan = await engine.PreviewAsync(default, 2026);
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic late-client check", default));
        Assert.Equal("cleanup_other_clients_connected", error.Code);
        Assert.True(writer.Connected);
        await concurrent.CloseAsync();
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.Equal(101m, await db.Funds.Where(f => f.Id == fund.Id).Select(f => f.Balance).SingleAsync());
        Assert.False(await db.MeterDevices.AnyAsync(d => d.IsAccountingBaseline));
        Assert.False(await db.ApplicationSettings.AnyAsync(s => s.Key == "finance.annual_accrual_hold.2026"));
        Assert.False(await db.AuditEvents.AnyAsync(e => e.Action == "finance.selective_history_cleared"));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RollbackProofRequiresExactOriginalDataAndNeverDeletes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new RejectBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default, 2026);
        await engine.VerifyRolledBackAsync(plan, default);
        await engine.VerifyRollbackRecoveryAfterStartAsync(plan, default);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.Single(await db.FinancialOperations.ToArrayAsync());
        Assert.False(await db.AuditEvents.AnyAsync(audit => audit.Action == "finance.selective_history_cleared"));
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        Assert.False(await db.ApplicationSettings.AnyAsync(setting => setting.Key == "finance.annual_accrual_hold.2026"));
        await db.Garages.Where(garage => garage.Id == seed.Garage.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(garage => garage.StartingBalance, 6501));
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyRolledBackAsync(plan, default));
        Assert.Equal("maintenance_rollback_unverified", error.Code);
        // A trusted receipt records proof made before restart. This post-start
        // check deliberately does not claim normal subsequent data stayed equal.
        await engine.VerifyRollbackRecoveryAfterStartAsync(plan, default);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RollbackProofRejectsCommitInvalidPlanAndActiveTransaction()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default, 2026);
        Assert.Equal("cleanup_plan_invalid", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.VerifyRolledBackAsync(plan with { Fingerprint = new string('a', 64) }, default))).Code);
        Assert.Equal("cleanup_plan_invalid", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.VerifyRollbackRecoveryAfterStartAsync(plan with { Fingerprint = new string('a', 64) }, default))).Code);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("cleanup_transaction_active", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                engine.VerifyRolledBackAsync(plan, default))).Code);
            Assert.Equal("cleanup_plan_invalid", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                engine.VerifyRollbackRecoveryAfterStartAsync(plan, default))).Code);
        }
        await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic committed operation", default);
        Assert.Equal("maintenance_commit_already_recorded", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.VerifyRolledBackAsync(plan, default))).Code);
        Assert.Equal("maintenance_commit_already_recorded", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.VerifyRollbackRecoveryAfterStartAsync(plan, default))).Code);
        Assert.Empty(await db.MeterReadings.ToArrayAsync());
        Assert.True(await db.ApplicationSettings.AnyAsync(setting => setting.Key == "finance.annual_accrual_hold.2026" && setting.BooleanValue));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PostCommitVerificationRejectsTamperedHoldAndPreservedData()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default, 2026);
        await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic verification", default);
        var relevantAudits = await db.AuditEvents.AsNoTracking().Where(e => e.Action == "finance.selective_history_cleared" || e.Action == "finance.annual_accrual_generation_held" || e.Action == "finance.meter_baseline_preserved").ToArrayAsync();
        Assert.Equal(plan.Baselines.Count + 2, relevantAudits.Length);
        Assert.All(relevantAudits.Where(e => e.Action != "finance.selective_history_cleared"), audit => Assert.Contains(plan.PlanId.ToString(), audit.MetadataJson!, StringComparison.OrdinalIgnoreCase));
        await engine.VerifyPostCommitAsync(plan, default);
        var incomeTypeId = await db.IncomeTypes.Select(type => type.Id).FirstAsync();
        var monthly = new Accrual
        {
            GarageId = seed.Garage.Id,
            IncomeTypeId = incomeTypeId,
            AccountingMonth = new(2026, 11, 1),
            AccountingYear = null,
            IsAnnualAccrual = false,
            DueDate = new(2026, 11, 30),
            OverdueFromDate = new(2026, 12, 1),
            Amount = 10m,
            Source = "synthetic-monthly"
        };
        db.Accruals.Add(monthly);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("cleanup_verification_failed", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyPostCommitAsync(plan, default))).Code);
        await engine.VerifyRecoveryAfterStartAsync(plan, default);
        await db.Accruals.Where(item => item.Id == monthly.Id).ExecuteDeleteAsync();
        var untouched = await db.MeterDevices.AsNoTracking().SingleAsync(device => device.Id == seed.Current.Id);
        var priorVersion = untouched.Version;
        var changedVersion = Guid.NewGuid();
        await db.MeterDevices.Where(device => device.Id == untouched.Id).ExecuteUpdateAsync(s => s.SetProperty(value => value.Version, changedVersion));
        Assert.Equal("cleanup_preservation_failed", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyPostCommitAsync(plan, default))).Code);
        await db.MeterDevices.Where(device => device.Id == untouched.Id).ExecuteUpdateAsync(s => s.SetProperty(value => value.Version, priorVersion));
        var audit = await db.AuditEvents.AsNoTracking().SingleAsync(e => e.Action == "finance.selective_history_cleared" && e.EntityId == plan.PlanId.ToString());
        var originalMetadata = audit.MetadataJson!;
        var alteredMetadata = originalMetadata.Replace(plan.Fingerprint, new string('0', 64), StringComparison.Ordinal);
        Assert.NotEqual(originalMetadata, alteredMetadata);
        await db.AuditEvents.Where(e => e.Id == audit.Id).ExecuteUpdateAsync(s => s.SetProperty(value => value.MetadataJson, alteredMetadata));
        Assert.Equal("maintenance_commit_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyPostCommitAsync(plan, default))).Code);
        Assert.Equal("maintenance_commit_unverified", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyRecoveryAfterStartAsync(plan, default))).Code);
        await db.AuditEvents.Where(e => e.Id == audit.Id).ExecuteUpdateAsync(s => s.SetProperty(value => value.MetadataJson, originalMetadata));
        await db.ApplicationSettings.Where(s => s.Key == "finance.annual_accrual_hold.2026")
            .ExecuteUpdateAsync(s => s.SetProperty(value => value.IntegerValue, 2027));
        Assert.Equal("cleanup_annual_policy_changed", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyPostCommitAsync(plan, default))).Code);
        Assert.Equal("cleanup_annual_policy_changed", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.VerifyRecoveryAfterStartAsync(plan, default))).Code);
    }

    [Fact]
    public async Task StagingCleanupAndHoldReleaseFailBeforeConnectionBackupOrTransaction()
    {
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=garagebalance_staging;Username=synthetic;Timeout=1").Options;
        await using var db = new GarageBalanceDbContext(options);
        var backup = new RecordingBackup();
        var cleanup = new SelectiveFinancialCleanupEngine(db, backup, TimeProvider.System);
        var release = new AnnualAccrualHoldReleaseEngine(db, backup, TimeProvider.System);
        var cleanupError = await Assert.ThrowsAsync<AccountingCleanupException>(() => cleanup.ExecuteAsync(null!, "", null!, Identity, "Synthetic approval", default));
        Assert.Equal("cleanup_service_exclusion_unverified", cleanupError.Code);
        var releaseError = await Assert.ThrowsAsync<AccountingCleanupException>(() => release.ReleaseAsync(null!, "", null!, Identity, "Synthetic approval", default));
        Assert.Equal("cleanup_service_exclusion_unverified", releaseError.Code);
        Assert.False(backup.Called);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void StagingOfflineProof_RejectsWrongTargetAndEveryUnknownOrRunningState()
    {
        const string inactive = "LoadState=loaded\nActiveState=inactive\nSubState=dead\nMainPID=0\nControlGroup=\nFragmentPath=/etc/systemd/system/garagebalance-staging.service\n";
        Assert.True(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive, 1, false));
        Assert.False(StagingOfflineProof.IsOffline("unrelated.service", null, "0", 0, inactive, 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, 3101, "0", 0, inactive, 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "1000", 0, inactive, 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 1, inactive, 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, null, 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive.Replace("inactive", "active"), 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive.Replace("FragmentPath=/etc/systemd/system/garagebalance-staging.service", "FragmentPath=/wrong"), 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive.Replace("MainPID=0", "MainPID=7"), 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive.Replace("ControlGroup=", "ControlGroup=/running"), 1, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive, 0, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive, 2, false));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive, 1, true));
        Assert.False(StagingOfflineProof.IsOffline(StagingOfflineProof.Service, null, "0", 0, inactive, 1, null));
    }

    [Fact]
    public void StagingTargetBinding_RequiresConfirmedMachineAddressLocalPostgresPortAndDatabase()
    {
        string[] addresses = ["127.0.0.1", "31.192.110.221"];
        Assert.True(StagingTargetBinding.IsAllowed("41765", addresses, "/var/run/postgresql", 5432, "garagebalance_staging"));
        Assert.True(StagingTargetBinding.IsAllowed("41765", addresses, "127.0.0.1", 5432, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("wrong", addresses, "127.0.0.1", 5432, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("41765", ["127.0.0.1"], "127.0.0.1", 5432, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("41765", addresses, "31.192.110.222", 5432, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("41765", addresses, "127.0.0.1,other", 5432, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("41765", addresses, "127.0.0.1", 5433, "garagebalance_staging"));
        Assert.False(StagingTargetBinding.IsAllowed("41765", addresses, "127.0.0.1", 5432, "another_database"));
        Assert.True(StagingTargetBinding.IsLocalServerAddress(null));
        Assert.True(StagingTargetBinding.IsLocalServerAddress("127.0.0.1"));
        Assert.True(StagingTargetBinding.IsLocalServerAddress("::1"));
        Assert.False(StagingTargetBinding.IsLocalServerAddress("31.192.110.221"));
        Assert.False(StagingTargetBinding.IsLocalServerAddress("invalid"));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ServerAddressUsesHostWithoutCidrAndRejectsNonlocalOrWrongPort()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var address = await db.Database.SqlQueryRaw<string>("SELECT host(inet_server_addr()) AS \"Value\"").SingleAsync();
        var nativeAddress = await db.Database.SqlQueryRaw<System.Net.IPAddress>("SELECT inet_server_addr() AS \"Value\"").SingleAsync();
        var actualPort = await db.Database.SqlQueryRaw<int>("SELECT current_setting('port')::int AS \"Value\"").SingleAsync();
        Assert.Equal(nativeAddress.ToString(), address);
        Assert.DoesNotContain('/', address);
        var loopback = System.Net.IPAddress.IsLoopback(nativeAddress);
        Assert.Equal(loopback, StagingTargetBinding.IsLocalServerAddress(address));
        Assert.Equal(loopback, await StagingTargetBinding.IsConnectedServerLocalAsync(db, actualPort, default));
        Assert.False(await StagingTargetBinding.IsConnectedServerLocalAsync(db, actualPort == 5432 ? 5433 : 5432, default));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExclusiveMaintenanceLockPreventsReadingOldAnnualPolicyBeforeCommit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var writer = database.CreateContext();
        await using var reader = database.CreateContext();
        var setting = new GarageBalance.Api.Domain.Settings.ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false };
        writer.Add(setting);
        await writer.SaveChangesAsync();
        writer.ChangeTracker.Clear();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE");
        await writer.ApplicationSettings.Where(item => item.Id == setting.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.BooleanValue, true));
        await writer.ApplicationSettings.Where(item => item.Key == "finance.annual_accrual_guard_epoch").ExecuteUpdateAsync(update => update.SetProperty(item => item.JsonValue, "{\"2026\":true}"));
        await reader.Database.OpenConnectionAsync();
        await reader.Database.ExecuteSqlRawAsync("SET lock_timeout = '250ms'");
        var blocked = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => reader.Database.ExecuteSqlRawAsync("SELECT \"BooleanValue\" FROM application_settings"));
        Assert.Equal("55P03", blocked.SqlState);
        await transaction.CommitAsync();
        Assert.True((await reader.ApplicationSettings.AsNoTracking().SingleAsync(item => item.Id == setting.Id)).BooleanValue);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ClientCanConnectAfterMaintenanceClientCountEvenWhileTablesAreLocked()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var maintenance = database.CreateContext();
        await using var tx = await maintenance.Database.BeginTransactionAsync();
        await maintenance.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE");
        var before = await maintenance.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM pg_stat_activity
            WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend'
            """).SingleAsync();
        Assert.Equal(0, before);
        await using var lateClient = database.CreateContext();
        await lateClient.Database.OpenConnectionAsync();
        // pg_stat_activity may cache a transaction-local snapshot, so prove the
        // late client is usable directly rather than relying on a second stats read.
        var result = await lateClient.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync();
        Assert.Equal(1, result);
        await tx.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExistingSerializableSnapshotCanReadOldPolicyAfterExclusiveLockIsReleased()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var writer = database.CreateContext();
        await using var reader = database.CreateContext();
        var setting = new GarageBalance.Api.Domain.Settings.ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = false };
        writer.Add(setting);
        await writer.SaveChangesAsync();
        writer.ChangeTracker.Clear();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("LOCK TABLE application_settings IN ACCESS EXCLUSIVE MODE");
        await writer.ApplicationSettings.Where(item => item.Id == setting.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.BooleanValue, true));
        await writer.ApplicationSettings.Where(item => item.Key == "finance.annual_accrual_guard_epoch").ExecuteUpdateAsync(update => update.SetProperty(item => item.JsonValue, "{\"2026\":true}"));
        await using var snapshot = await reader.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await reader.Database.ExecuteSqlRawAsync("SELECT 1");
        var pendingRead = reader.ApplicationSettings.AsNoTracking().SingleAsync(item => item.Id == setting.Id);
        await Task.Delay(250);
        Assert.False(pendingRead.IsCompleted);
        await transaction.CommitAsync();
        var oldValue = await pendingRead;
        Assert.False(oldValue.BooleanValue);
        await snapshot.RollbackAsync();
    }

    [Theory]
    [InlineData("selective-financial-cleanup")]
    [InlineData("SELECTIVE-FINANCIAL-CLEANUP")]
    [InlineData("ANNUAL-ACCRUAL-RELEASE")]
    public async Task CliRejectsWrongServiceOrPortRegardlessOfCommandCase(string command)
    {
        var plan = Path.Combine(Path.GetTempPath(), "synthetic-plan.json");
        var receipt = Path.Combine(Path.GetTempPath(), "synthetic-receipt.json");
        var prefix = new[] { command, "--execute", "--plan", plan, "--confirm", new string('a', 64), "--backup-receipt", receipt,
            "--expected-database", "garagebalance_staging", "--reason", "Synthetic approval" };
        var config = new ConfigurationBuilder().Build();
        Assert.NotEqual(0, await AccountingCleanupCommands.RunAsync([.. prefix, "--service", "unrelated.service"], config, default));
        Assert.NotEqual(0, await AccountingCleanupCommands.RunAsync([.. prefix, "--application-port", "3101"], config, default));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_FundResidualOrUnknownKindBlocksPreviewWithoutChangingHistory()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        await db.Funds.Where(f => f.Id == fund.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Balance, 102m));
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        Assert.Equal("cleanup_fund_residual", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(default))).Code);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        await db.Funds.Where(f => f.Id == fund.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Balance, 101m));
        await db.FundOperations.Where(o => o.FundId == fund.Id && !o.IsCanceled && o.Amount == 1m)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.OperationKind, "unknown"));
        Assert.Equal("cleanup_fund_residual", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(default))).Code);
        Assert.Equal(101m, (await db.Funds.SingleAsync(f => f.Id == fund.Id)).Balance);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ServiceRestartsDuringTransactionRollsBackAllChangesAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var checks = 0;
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System,
            confirmStopped: _ => Task.FromResult(++checks == 1));
        var plan = await engine.PreviewAsync(default, 2026);
        Assert.Equal("cleanup_host_started", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic restart race", default))).Code);
        Assert.Equal(2, checks);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.Equal(101m, (await db.Funds.SingleAsync(f => f.Id == fund.Id)).Balance);
        Assert.False(await db.ApplicationSettings.AnyAsync(s => s.Key == "finance.annual_accrual_hold.2026"));
        Assert.False(await db.AuditEvents.AnyAsync(a => a.Action == "finance.selective_history_cleared"));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_FundChangeDuringDeletionRollsBackInsteadOfZeroingResidual()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.synthetic_fund_drift() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN UPDATE funds SET "Balance"="Balance"+1 WHERE "Id"=OLD."FundId"; RETURN OLD; END $$;
            CREATE TRIGGER synthetic_fund_drift AFTER DELETE ON fund_operations
            FOR EACH ROW EXECUTE FUNCTION public.synthetic_fund_drift();
            """);
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        Assert.Equal("cleanup_fund_changed_during_delete", (await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic fund race", default))).Code);
        Assert.Equal(101m, (await db.Funds.SingleAsync(f => f.Id == fund.Id)).Balance);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.AuditEvents.AnyAsync(a => a.Action == "finance.selective_history_cleared"));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualReleaseRequiresExactFreshPlanBackupAndOfflinePrivilegeAndAuditsOnlyOneYear()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        db.AddRange(new GarageBalance.Api.Domain.Settings.ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = true },
            new GarageBalance.Api.Domain.Settings.ApplicationSetting { Key = "finance.annual_accrual_hold.2028", BooleanValue = true });
        (await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch")).JsonValue = "{\"2026\":true,\"2028\":true}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var engine = new AnnualAccrualHoldReleaseEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(2026, default);
        var receipt = new AccountingCleanupBackupReceipt(1, plan.Source, "synthetic-test-only", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);
        Assert.Equal("cleanup_permission_denied", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ReleaseAsync(plan, plan.Fingerprint, receipt, new(false, true, "test"), "Synthetic release", default))).Code);
        Assert.Equal("cleanup_plan_invalid", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ReleaseAsync(plan, "wrong", receipt, Identity, "Synthetic release", default))).Code);
        Assert.Equal("synthetic_backup_rejected", (await Assert.ThrowsAsync<AccountingCleanupException>(() => new AnnualAccrualHoldReleaseEngine(db, new RejectBackup(), TimeProvider.System).ReleaseAsync(plan, plan.Fingerprint, receipt, Identity, "Synthetic release", default))).Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AnnualAccrualHoldReleaseEngine(db, new AcceptBackup(), TimeProvider.System, new FailAudit()).ReleaseAsync(plan, plan.Fingerprint, receipt, Identity, "Synthetic audit failure", default));
        Assert.True((await db.ApplicationSettings.SingleAsync(s => s.Id == plan.SettingId)).BooleanValue);
        db.ChangeTracker.Clear();
        await engine.ReleaseAsync(plan, plan.Fingerprint, receipt, Identity, "Synthetic approved release", default);
        Assert.False((await db.ApplicationSettings.SingleAsync(s => s.Id == plan.SettingId)).BooleanValue);
        Assert.True((await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_hold.2028")).BooleanValue);
        Assert.Empty(await db.Accruals.ToArrayAsync());
        Assert.Single(await db.AuditEvents.Where(a => a.Action == "finance.annual_accrual_generation_resumed").ToArrayAsync());
        Assert.Equal("annual_hold_not_active", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(2026, default))).Code);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualReleaseRejectsRowContentDriftWithoutVersionChange()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        db.Add(new GarageBalance.Api.Domain.Settings.ApplicationSetting { Key = "finance.annual_accrual_hold.2026", BooleanValue = true });
        (await db.ApplicationSettings.SingleAsync(s => s.Key == "finance.annual_accrual_guard_epoch")).JsonValue = "{\"2026\":true}";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var engine = new AnnualAccrualHoldReleaseEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(2026, default);
        await db.ApplicationSettings.Where(s => s.Id == plan.SettingId).ExecuteUpdateAsync(s => s.SetProperty(x => x.JsonValue, "{\"changed\":true}"));
        var receipt = new AccountingCleanupBackupReceipt(1, plan.Source, "synthetic-test-only", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);
        Assert.Equal("cleanup_preview_stale", (await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.ReleaseAsync(plan, plan.Fingerprint, receipt, Identity, "Synthetic stale release", default))).Code);
        Assert.True((await db.ApplicationSettings.SingleAsync(s => s.Id == plan.SettingId)).BooleanValue);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExplicitAnnualPolicyIsAtomicPreservesSettingsAndAllowsRestart()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        var beforeSettings = await db.ApplicationSettings.CountAsync();
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default, 2026);
        Assert.Equal(2026, plan.HeldAnnualYear);
        Assert.Equal(beforeSettings, await db.ApplicationSettings.CountAsync());
        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic approved annual decision", default);
        Assert.False(result.MustRemainOffline);
        Assert.Equal(beforeSettings + 1, await db.ApplicationSettings.CountAsync());
        var hold = await db.ApplicationSettings.SingleAsync(setting => setting.Key == "finance.annual_accrual_hold.2026");
        Assert.True(hold.BooleanValue);
        Assert.Equal(2026, hold.IntegerValue);
        Assert.Contains(plan.PlanId.ToString(), hold.JsonValue);
        Assert.Single(await db.AuditEvents.Where(audit => audit.Action == "finance.annual_accrual_generation_held").ToArrayAsync());
        Assert.Equal(980m, (await new EfMeterReadingRepository(db).GetYearPageAsync(2026, MeterKinds.Electricity, 0, 25, default)).Garages.Single(g => g.Id == seed.Garage.Id).StartValue);
        Assert.Empty(await db.Accruals.ToArrayAsync());
        var conflict = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(default, 2026));
        Assert.Equal("cleanup_annual_policy_exists", conflict.Code);
        Assert.Equal(beforeSettings + 1, await db.ApplicationSettings.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AnnualPolicyRollsBackTogetherWithCleanupOnAuditFailure()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System, new FailAudit());
        var plan = await engine.PreviewAsync(default, 2026);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic atomic hold", default));
        Assert.False(await db.ApplicationSettings.AnyAsync(setting => setting.Key == "finance.annual_accrual_hold.2026"));
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_ExactPlanDeletesAllSixRetainsOpeningHistoryAndSupportsCardGridCalculationAndEditing()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        Assert.Equal(6, plan.Tables.Single(table => table.Table == "meter_readings").Rows.Count);
        Assert.Equal(2, plan.Baselines.Count);
        Assert.Equal(980m, plan.Baselines.Single(item => item.GarageId == seed.Garage.Id).Value);
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        Assert.Equal(6, await db.MeterReadings.CountAsync());

        var result = await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic approved cleanup", default);
        Assert.True(result.MustRemainOffline);
        Assert.Equal(20, result.RemovedRows);
        Assert.Empty(await db.MeterReadings.ToArrayAsync());
        Assert.Empty(await db.Accruals.ToArrayAsync());
        Assert.Empty(await db.FinancialOperations.ToArrayAsync());
        Assert.Empty(await db.AccrualPaymentAllocations.ToArrayAsync());
        Assert.Empty(await db.FundOperations.ToArrayAsync());
        Assert.Empty(await db.SupplierAccruals.ToArrayAsync());
        Assert.Equal(0m, (await db.Funds.SingleAsync(item => item.Id == fund.Id)).Balance);
        Assert.True(await db.Suppliers.AnyAsync(item => item.Name == "Synthetic preserved supplier"));
        Assert.True(await db.MeterDevices.AnyAsync(device => device.Id == seed.Original.Id && device.IsAccountingBaseline && device.FinalValue == 4578));
        Assert.Equal(6500m, (await db.Garages.SingleAsync(garage => garage.Id == seed.Garage.Id)).StartingBalance);
        Assert.Single(await db.AuditEvents.Where(audit => audit.Action == "finance.selective_history_cleared").ToArrayAsync());
        await PreservedMeterBaselineTests.VerifyAfterCleanupAsync(db, seed);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RefusesChangedContentEvenWithoutVersionRotation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        await db.MeterReadings.Where(reading => reading.CurrentValue == 1540)
            .ExecuteUpdateAsync(setters => setters.SetProperty(reading => reading.CurrentValue, 1541));
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic cleanup", default));
        Assert.Equal("cleanup_preview_stale", error.Code);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RollsBackDeletesPinsAndFundChangesIfAuditFails()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        var fund = await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System, new FailAudit());
        var plan = await engine.PreviewAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic cleanup", default));
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        Assert.Equal(101m, (await db.Funds.SingleAsync(item => item.Id == fund.Id)).Balance);
        Assert.Equal(2, await db.AccrualPaymentAllocations.CountAsync());
        Assert.False(await db.AuditEvents.AnyAsync(audit => audit.Action == "finance.selective_history_cleared"));
        Assert.Null(db.Database.CurrentTransaction);
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RequiresExactPlanOfflinePrivilegeBackupAndProtectsPhysicalReplacementReadings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new RejectBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        foreach (var identity in new[] { new AccountingCleanupExecutionIdentity(false, true, "test"), new(true, false, "test") })
        {
            var denied = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
                engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), identity, "Synthetic cleanup", default));
            Assert.Equal("cleanup_permission_denied", denied.Code);
        }
        var invalid = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, "wrong", Receipt(plan), Identity, "Synthetic cleanup", default));
        Assert.Equal("cleanup_plan_invalid", invalid.Code);
        var backup = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic cleanup", default));
        Assert.Equal("synthetic_backup_rejected", backup.Code);
        await db.MeterDevices.Where(device => device.Id == seed.Current.Id).ExecuteUpdateAsync(setters =>
            setters.SetProperty(device => device.ReplacementReadingId, plan.Tables.Single(table => table.Table == "meter_readings").Rows[0].Id));
        var history = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(default));
        Assert.Equal("cleanup_replacement_reading_protected", history.Code);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
    }

    [Fact]
    public void Cli_DefaultsToPreviewAndRequiresExistingHostAndBackupGates()
    {
        var path = Path.Combine(Path.GetTempPath(), "synthetic-selective-plan.json");
        Assert.False(AccountingCleanupCommandOptions.Parse(["selective-financial-plan", "--output", path]).Execute);
        Assert.False(AccountingCleanupCommandOptions.Parse(["selective-financial-cleanup", "--output", path]).Execute);
        Assert.Throws<AccountingCleanupException>(() => AccountingCleanupCommandOptions.Parse(["selective-financial-cleanup", "--execute"]));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_CliPreviewWritesPrivateManifestWithoutChangingReadings()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext()) await PreservedMeterBaselineTests.SeedAsync(seedContext);
        var path = Path.Combine(Path.GetTempPath(), $"garagebalance-selective-{Guid.NewGuid():N}.json");
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }).Build();
            var result = await AccountingCleanupCommands.RunAsync(["selective-financial-plan", "--output", path], configuration, default);
            Assert.Equal(0, result);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(6, document.RootElement.GetProperty("tables").EnumerateArray().Single(table => table.GetProperty("table").GetString() == "meter_readings")
                .GetProperty("rows").GetArrayLength());
            await using var verify = database.CreateContext();
            Assert.Equal(6, await verify.MeterReadings.CountAsync());
            Assert.False(await verify.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RefusesUnreviewedPayoutScopeWithoutRemovingAnything()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        await SeedFinancialHistoryAsync(db, seed);
        db.ChangeTracker.Clear();
        await db.FinancialOperations.ExecuteUpdateAsync(setters => setters.SetProperty(operation => operation.OperationKind, FinancialOperationKinds.Expense));
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System).PreviewAsync(default));
        Assert.Equal("cleanup_unreviewed_operations", error.Code);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.Equal(1, await db.FinancialOperations.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_RejectsMasterDataDriftAndNewInboundReadingDependency()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        await db.Garages.Where(garage => garage.Id == seed.Garage.Id).ExecuteUpdateAsync(setters => setters.SetProperty(garage => garage.StartingBalance, 6501));
        var stale = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic cleanup", default));
        Assert.Equal("cleanup_preview_stale", stale.Code);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE public.synthetic_cleanup_dependency (\"Id\" uuid PRIMARY KEY, \"ReadingId\" uuid REFERENCES meter_readings(\"Id\"))");
        var schema = await Assert.ThrowsAsync<AccountingCleanupException>(() => engine.PreviewAsync(default));
        Assert.Equal("cleanup_schema_unreviewed", schema.Code);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
    }

    [PostgreSqlFact]
    public async Task PostgreSql_PreservationFailureInsideTransactionRestoresOpeningsReadingsAndPins()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        db.ChangeTracker.Clear();
        // Synthetic fault: a database side effect changes preserved master data while
        // the exact listed reading IDs are deleted. Post-write verification must catch it.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.synthetic_cleanup_master_change() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                UPDATE garages SET "StartingBalance"="StartingBalance"+1 WHERE "Id"=OLD."GarageId";
                RETURN OLD;
            END $$;
            CREATE TRIGGER synthetic_cleanup_master_change AFTER DELETE ON meter_readings
                FOR EACH ROW EXECUTE FUNCTION public.synthetic_cleanup_master_change();
            """);
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        var error = await Assert.ThrowsAsync<AccountingCleanupException>(() =>
            engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic preservation fault", default));
        Assert.Equal("cleanup_preservation_failed", error.Code);
        Assert.Equal(6500m, (await db.Garages.SingleAsync(garage => garage.Id == seed.Garage.Id)).StartingBalance);
        Assert.Equal(6, await db.MeterReadings.CountAsync());
        Assert.False(await db.MeterDevices.AnyAsync(device => device.IsAccountingBaseline));
        Assert.False(await db.AuditEvents.AnyAsync(audit => audit.Action == "finance.selective_history_cleared"));
    }

    [PostgreSqlFact]
    public async Task PostgreSql_AuditRecordsEachBaselineAsScalarValuesWithThousandthPrecision()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        var seed = await PreservedMeterBaselineTests.SeedAsync(db);
        seed.Original.InitialValue = 980.125m;
        seed.Garage.InitialElectricityMeterValue = 980.125m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var engine = new SelectiveFinancialCleanupEngine(db, new AcceptBackup(), TimeProvider.System);
        var plan = await engine.PreviewAsync(default);
        await engine.ExecuteAsync(plan, plan.Fingerprint, Receipt(plan), Identity, "Synthetic precise opening", default);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "finance.meter_baseline_preserved" && item.EntityId == seed.Original.Id.ToString());
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("980.125", metadata.RootElement.GetProperty("baselineValue").GetString());
        Assert.Equal("2026-08-01", metadata.RootElement.GetProperty("baselineStartsOn").GetString());
        Assert.Equal(seed.Original.Id.ToString(), metadata.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal(MeterKinds.Electricity, metadata.RootElement.GetProperty("meterKind").GetString());
        Assert.Equal(2, await db.AuditEvents.CountAsync(item => item.Action == "finance.meter_baseline_preserved"));
        Assert.Equal(980.125m, (await db.MeterDevices.SingleAsync(item => item.Id == seed.Original.Id)).InitialValue);
    }

    private static AccountingCleanupBackupReceipt Receipt(SelectiveFinancialCleanupPlan plan) =>
        new(1, plan.Source, "synthetic-test-only", new string('a', 64), plan.CreatedAtUtc, plan.CreatedAtUtc, plan.Source.SchemaFingerprint, true);

    private static async Task<Fund> SeedFinancialHistoryAsync(GarageBalanceDbContext db, PreservedMeterBaselineTests.Seed seed)
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
    private sealed class AcceptBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class ConnectOnAudit(GarageBalanceDbContext db, Npgsql.NpgsqlConnection other) : IAuditEventWriter
    {
        public bool Connected { get; private set; }
        public AuditEvent? Add(AuditEventWriteRequest request)
        {
            if (!Connected) { other.Open(); Connected = true; }
            return new AuditEventWriter(db).Add(request);
        }
    }
    private sealed class RecordingBackup : IAccountingCleanupBackupVerifier
    {
        public bool Called { get; private set; }
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken token)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
    private sealed class RejectBackup : IAccountingCleanupBackupVerifier
    {
        public Task VerifyAsync(AccountingCleanupBackupReceipt receipt, AccountingCleanupSource source, DateTimeOffset now, CancellationToken token) =>
            throw new AccountingCleanupException("synthetic_backup_rejected", "Synthetic backup failure");
    }
    private sealed class FailAudit : IAuditEventWriter
    {
        public AuditEvent? Add(AuditEventWriteRequest request) => throw new InvalidOperationException("Synthetic audit failure");
    }
}
