using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

try
{
    if (!OperatingSystem.IsLinux() || args.Length is < 1 or > 2) return 64;
    var fixture = new ProtocolFixture();
    return await fixture.RunAsync(args[0], args.ElementAtOrDefault(1));
}
catch (Exception error)
{
    Console.Error.WriteLine("fixtureFailure=" + error.GetType().Name);
    if (error.Message.StartsWith("fixture_", StringComparison.Ordinal)) Console.Error.WriteLine("fixtureCode=" + error.Message.Split('\n')[0]);
    if (error is AccountingCleanupException cleanup) Console.Error.WriteLine("fixtureCode=" + cleanup.Code);
    return 1;
}

[SupportedOSPlatform("linux")]
internal sealed partial class ProtocolFixture
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string root = Environment.GetEnvironmentVariable("GB_FIXTURE_STATE") ?? "";
    private readonly string barrierDirectory = Environment.GetEnvironmentVariable("GB_FIXTURE_BARRIER") ?? "";
    private readonly string prefix = Environment.GetEnvironmentVariable("GB_FIXTURE_PREFIX") ?? "";
    private readonly string connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "";
    private readonly Dictionary<string, string> units;
    private string PlanPath => Path.Combine(root, "plan.json");
    private string ReceiptPath => Path.Combine(root, "backup-receipt.json");

    public ProtocolFixture()
    {
        if (!Regex.IsMatch(root, @"\A/run/gb-maint-integration\.[A-Za-z0-9]{8}\z") ||
            !Regex.IsMatch(barrierDirectory, @"\A/run/gb-barrier-dotnet\.[A-Za-z0-9]{8}\z") ||
            !Regex.IsMatch(prefix, @"\Agb-maint-integration-[a-f0-9]{8}\z") ||
            new DirectoryInfo(root).LinkTarget is not null || new DirectoryInfo(barrierDirectory).LinkTarget is not null)
            throw new InvalidOperationException("fixture_identity_invalid");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host != Path.Combine(root, "pg", "socket") || settings.Port != 55483 || settings.Username != "postgres" ||
            settings.Password is not null && settings.Password.Length != 0 ||
            !Regex.IsMatch(settings.Database ?? "", @"\Agaragebalance_it_protocol_[a-f0-9]{8}\z") || settings.Pooling)
            throw new InvalidOperationException("fixture_database_invalid");
        units = new()
        {
            [StagingOfflineProof.Service] = prefix + "-api.service",
            ["garagebalance-healthcheck.service"] = prefix + "-health.service",
            ["garagebalance-healthcheck.timer"] = prefix + "-health.timer",
            ["garagebalance-storage-sync.timer"] = prefix + "-storage.timer",
            ["garagebalance-storage-tool@delta-sync.service"] = prefix + "-storage.service"
        };
    }

    private GarageBalanceDbContext Context(string? target = null) => new(new DbContextOptionsBuilder<GarageBalanceDbContext>()
        .UseNpgsql(target ?? connection).Options);

    public async Task<int> RunAsync(string command, string? fault)
    {
        if (command.StartsWith("annual-", StringComparison.Ordinal)) return await RunAnnualAsync(command, fault);
        switch (command)
        {
            case "local-files":
                var fileTests = new GarageBalance.Api.Tests.Storage.LocalReplicaRecoveryFilesTests();
                await fileTests.PublishesOnlyExactMissingArchivesAndAnExplicitRetryVerifiesExistingCopies();
                await fileTests.PermissionFailureAndCancellationPublishNothing();
                await fileTests.PartialPublicationIsRetainedAndFreshValidationCanCompleteTheRemainingFiles();
                foreach (var method in typeof(GarageBalance.Api.Tests.Storage.LocalReplicaRecoveryFilesTests).GetMethods())
                    foreach (var data in method.GetCustomAttributes<Xunit.InlineDataAttribute>())
                        foreach (var values in data.GetData(method))
                            await (Task)method.Invoke(fileTests, values)!;
                Console.WriteLine("fixtureLocalRecoveryFiles=passed");
                break;
            case "api":
                var entry = typeof(GarageBalanceDbContext).Assembly.EntryPoint ?? throw new InvalidOperationException("fixture_api_entry_missing");
                var entryResult = entry.Invoke(null, [new[] { "--applicationName", "GarageBalance.Api", "--contentRoot", AppContext.BaseDirectory }]);
                if (entryResult is Task task) await task;
                return entryResult is int code ? code : 0;
            case "storage":
                return await StorageToolProgram.RunAsync(["delta-sync", "--execute", "--checkpoint", Path.Combine(root, "storage-checkpoint.json")], default);
            case "storage-copy":
                return await StorageToolProgram.RunAsync(["copy", "--execute", "--checkpoint", Path.Combine(root, "storage-checkpoint.json")], default);
            case "seed":
                await using (var db = Context())
                {
                    await db.Database.MigrateAsync();
                    var data = await FixtureData.SeedAsync(db);
                    await FixtureData.SeedFinancialHistoryAsync(db, data);
                }
                break;
            case "sandbox":
                Assert.False(File.Exists("/opt/garagebalance-staging/api/GarageBalance.Api"), "fixture_working_api_visible");
                // systemd overmounts an inaccessible regular file with an empty
                // inode. Only metadata is inspected; credentials are never read.
                if (File.Exists("/etc/garagebalance-staging.env"))
                    Assert.Equal(0L, new FileInfo("/etc/garagebalance-staging.env").Length);
                Assert.False(File.Exists("/run/postgresql/.s.PGSQL.5432"));
                Assert.DoesNotContain(NetworkInterface.GetAllNetworkInterfaces().SelectMany(i => i.GetIPProperties().UnicastAddresses),
                    a => a.Address.ToString() == "31.192.110.221");
                await Assert.ThrowsAsync<InvalidOperationException>(() => StagingBarrierQualification.VerifyInstalledAsync(default));
                break;
            case "health": await HealthAsync(default); break;
            case "stage": await StageAsync(fault); break;
            case "backup": await BackupAndRestoreAsync(); break;
            case "execute": await ExecuteAsync(fault); break;
            case "recover": await RecoverAsync(); break;
            case "abort": await AbortAsync(); break;
            case "assert": await AssertOutcomeAsync(fault ?? throw new InvalidOperationException("fixture_outcome_missing")); break;
            default: return 64;
        }
        Console.WriteLine("fixtureCommand=" + command + ":ok");
        return 0;
    }

    // Only identities are mapped. All state, stop/start jobs and process draining
    // are real systemd operations against this fixture's own units.
    private async Task<(int ExitCode, string Output)> SystemctlAsync(string[] arguments, CancellationToken token)
    {
        if (arguments[0] is not ("show" or "start" or "stop" or "reset-failed" or "list-units")) throw new InvalidOperationException("fixture_verb_invalid");
        var mapped = arguments.Select(value => value == "garagebalance-storage-tool@*.service" ? units["garagebalance-storage-tool@delta-sync.service"] :
            units.GetValueOrDefault(value, value)).ToArray();
        if (mapped.Any(value => value.StartsWith("garagebalance-", StringComparison.Ordinal))) throw new InvalidOperationException("fixture_working_unit_forbidden");
        var result = await AccountingCleanupHostEnvironment.RunAsync("/usr/bin/systemctl", mapped, token);
        var output = result.Output;
        foreach (var pair in units)
        {
            output = output.Replace("/run/systemd/system/" + pair.Value, "/etc/systemd/system/" + pair.Key, StringComparison.Ordinal);
            output = output.Replace(pair.Value, pair.Key, StringComparison.Ordinal);
        }
        return (result.ExitCode, output);
    }

    private StagingServiceLifecycle Lifecycle() => new(SystemctlAsync, HealthAsync, allowFailedForRecovery: true, offlineProof: OfflineAsync);

    private async Task OfflineAsync(CancellationToken token)
    {
        var state = await SystemctlAsync(["show", StagingOfflineProof.Service, "--property=ActiveState", "--property=MainPID", "--property=ControlGroup", "--all"], token);
        if (state.ExitCode != 0 || !state.Output.Split('\n').Contains("ActiveState=inactive") ||
            !state.Output.Split('\n').Contains("MainPID=0") || !state.Output.Split('\n').Contains("ControlGroup=") ||
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == 33101))
            throw new InvalidOperationException("fixture_api_not_drained");
    }

    private async Task HealthAsync(CancellationToken token)
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:33101/health/ready");
                request.Headers.Host = "sgk.blagodaty.ru";
                using var response = await http.SendAsync(request, token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(250, token);
        }
        throw new InvalidOperationException("fixture_api_unhealthy");
    }

    private LinuxMaintenanceBarrierHost Host(Guid id, string hash, StagingServiceLifecycle lifecycle,
        Func<bool> active, Func<MaintenanceParticipantState?> participants, Func<CancellationToken, Task>? verify = null,
        Func<CancellationToken, Task>? restore = null) => LinuxMaintenanceBarrierHost.CreateFixture(barrierDirectory, id, hash,
        lifecycle.CaptureStateAsync, lifecycle.StopAndDrainAsync, lifecycle.VerifyOfflineAsync, verify ?? (_ => Task.CompletedTask),
        restore ?? lifecycle.RestoreAsync, active, participants, lifecycle.RestoreParticipantsAsync);

    private async Task StageAsync(string? fault)
    {
        var lifecycle = Lifecycle();
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(root, "operation-id"), id.ToString("D"));
        using var host = Host(id, new string('0', 64), lifecycle, () => lifecycle.ServiceWasActive, () => lifecycle.ParticipantState);
        await using var db = Context();
        var engine = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System);
        try
        {
            await StagingMaintenancePreparation.PrepareAsync(host, id, (operation, token) =>
            {
                if (fault == "preparing") throw new IOException("Synthetic preparation failure");
                return engine.PreviewAsync(token, 2026, operation);
            }, async (plan, token) =>
            {
                Assert.True(lifecycle.ServiceWasActive);
                Assert.True(lifecycle.ParticipantState.HealthTimerWasActive && lifecycle.ParticipantState.StorageTimerWasActive);
                Assert.Contains("garagebalance-storage-tool@delta-sync.service", lifecycle.ParticipantState.ActiveStorageUnits);
                await WriteAsync(PlanPath, plan, token);
            }, default, token => host.TransitionPreparationAsync("preparing", null, token), async (plan, token) =>
            {
                await host.SyncPreparedPlanDirectoryAsync(PlanPath, token);
                await host.TransitionPreparationAsync("prepared", plan.Fingerprint, token);
            });
        }
        catch (IOException) when (fault == "preparing")
        {
            Assert.False(File.Exists(PlanPath));
            Console.WriteLine("fixturePreparationFailure=retained");
        }
    }

    private async Task BackupAndRestoreAsync()
    {
        var plan = await ReadAsync<SelectiveFinancialCleanupPlan>(PlanPath);
        var settings = new NpgsqlConnectionStringBuilder(connection);
        var path = Path.Combine(root, "precleanup.pgdump");
        var created = DateTimeOffset.UtcNow;
        await NativeAsync("pg_dump", ["--format=custom", "--file=" + path, "--host=" + settings.Host, "--port=55483", "--username=postgres", "--dbname=" + settings.Database]);
        var restoredName = "garagebalance_it_restore_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
        await using (var adminDb = new NpgsqlConnection(admin.ConnectionString))
        {
            await adminDb.OpenAsync();
            await using (var create = new NpgsqlCommand("CREATE DATABASE \"" + restoredName + "\"", adminDb)) await create.ExecuteNonQueryAsync();
            try
            {
                await NativeAsync("pg_restore", ["--exit-on-error", "--host=" + settings.Host, "--port=55483", "--username=postgres", "--dbname=" + restoredName, path]);
                await using var restored = Context(new NpgsqlConnectionStringBuilder(connection) { Database = restoredName }.ConnectionString);
                var copy = await new SelectiveFinancialCleanupEngine(restored, new AccountingCleanupBackupVerifier(), TimeProvider.System).PreviewAsync(default, 2026);
                Assert.Equal(plan.Source.SchemaFingerprint, copy.Source.SchemaFingerprint);
                Assert.Equal(JsonSerializer.Serialize(plan.Tables, Json), JsonSerializer.Serialize(copy.Tables, Json));
                Assert.Equal(JsonSerializer.Serialize(plan.MutableRows, Json), JsonSerializer.Serialize(copy.MutableRows, Json));
                Assert.Equal(JsonSerializer.Serialize(plan.Baselines, Json), JsonSerializer.Serialize(copy.Baselines, Json));
                Assert.Equal(JsonSerializer.Serialize(plan.PreservedHashes, Json), JsonSerializer.Serialize(copy.PreservedHashes, Json));
            }
            finally
            {
                await using var drop = new NpgsqlCommand("DROP DATABASE \"" + restoredName + "\" WITH (FORCE)", adminDb);
                await drop.ExecuteNonQueryAsync();
            }
        }
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        await WriteAsync(ReceiptPath, new AccountingCleanupBackupReceipt(1, plan.Source, path, hash, created,
            DateTimeOffset.UtcNow, plan.Source.SchemaFingerprint, true), default);
        Console.WriteLine("fixtureRestore=all_manifest_tables_verified");
    }

    private async Task ExecuteAsync(string? fault)
    {
        var plan = await ReadAsync<SelectiveFinancialCleanupPlan>(PlanPath);
        var backup = await ReadAsync<AccountingCleanupBackupReceipt>(ReceiptPath);
        var lifecycle = Lifecycle();
        MaintenanceRecoveryState? original = null;
        using var host = Host(plan.PlanId, plan.Fingerprint, lifecycle, () => original!.ServiceWasActive, () => original!.Participants,
            async token =>
            {
                await using var verify = Context();
                await new SelectiveFinancialCleanupEngine(verify, new AccountingCleanupBackupVerifier(), TimeProvider.System).VerifyPostCommitAsync(plan, token);
            }, async token =>
            {
                if (fault == "after-receipt") await SignalAsync(fault, token);
                await lifecycle.RestoreRecordedAsync(original!.ServiceWasActive, token, original.Participants);
                if (fault == "after-start") throw new IOException("Synthetic health completion failure");
            });
        try
        {
            await MaintenanceBarrierCoordinator.ExecutePreparedAsync(host, async token =>
            {
                Assert.Equal("prepared", host.ReadPreparationProgress()?.Phase);
                Assert.Equal(plan.Fingerprint, host.ReadPreparationProgress()?.Fingerprint);
                var files = host.ReadRecoveryFiles();
                original = MaintenanceRecoveryInspection.Inspect(files.Marker, files.Receipt, 0, files.ParticipantsPermitted);
                Assert.False(original.ReceiptPresent);
                host.AdoptRecoveryFiles(original);
                await lifecycle.CaptureStateAsync(token);
            }, async token =>
            {
                await host.TransitionPreparationAsync("executing", plan.Fingerprint, token);
                host.VerifyHeldProtocol(plan.PlanId, plan.Fingerprint);
                Assert.Throws<InvalidOperationException>(() => host.VerifyProductionExecutionAuthority(plan.PlanId, plan.Fingerprint));
            }, async token =>
            {
                await using var db = Context();
                var engine = new SelectiveFinancialCleanupEngine(db, new AccountingCleanupBackupVerifier(), TimeProvider.System,
                    auditWriter: fault == "before-commit" ? new PausingAudit(db, () => SignalAsync(fault, default).GetAwaiter().GetResult()) : null,
                    confirmStopped: async ct => { await lifecycle.VerifyOfflineAsync(ct); return true; });
                var result = await engine.ExecuteAsync(plan, plan.Fingerprint, backup, new(true, true, "isolated-fixture"), "Synthetic reviewed cleanup", token);
                Assert.False(result.MustRemainOffline);
                if (fault == "after-commit") await SignalAsync(fault, token);
            }, default);
        }
        catch (IOException) when (fault == "after-start") { Console.WriteLine("fixtureStartFailure=receipt_retained"); }
    }

    private async Task RecoverAsync()
    {
        var plan = await ReadAsync<SelectiveFinancialCleanupPlan>(PlanPath);
        var lifecycle = Lifecycle();
        MaintenanceRecoveryState? original = null;
        using var host = Host(plan.PlanId, plan.Fingerprint, lifecycle, () => original!.ServiceWasActive, () => original!.Participants);
        await using var db = Context();
        var outcome = await StagingRecoveryRunner.ResumeAsync(host, lifecycle, db, plan, state => original = state, default);
        Console.WriteLine("fixtureRecovery=" + outcome);
    }

    private async Task AbortAsync()
    {
        var id = Guid.ParseExact(await File.ReadAllTextAsync(Path.Combine(root, "operation-id")), "D");
        var lifecycle = Lifecycle();
        MaintenanceRecoveryState? original = null;
        using var host = Host(id, new string('0', 64), lifecycle, () => original!.ServiceWasActive, () => original!.Participants);
        await StagingRecoveryRunner.AbortPreparationAsync(host, lifecycle, state => original = state, default);
    }

    private async Task AssertOutcomeAsync(string outcome)
    {
        await HealthAsync(default);
        await using var db = Context();
        var committed = outcome == "committed";
        Assert.Equal(committed ? 0 : 6, await db.MeterReadings.CountAsync());
        Assert.Equal(committed ? 0 : 1, await db.FinancialOperations.CountAsync());
        Assert.Equal(committed ? 0 : 2, await db.Accruals.CountAsync());
        Assert.Equal(committed ? 0 : 4, await db.SupplierAccruals.CountAsync());
        Assert.Equal(committed ? 1 : 0, await db.AuditEvents.CountAsync(e => e.Action == "finance.selective_history_cleared"));
        Assert.Equal(committed, await db.ApplicationSettings.AnyAsync(s => s.Key == "finance.annual_accrual_hold.2026" && s.BooleanValue));
        var garage = await db.Garages.SingleAsync(g => g.Number == "SYNTHETIC-BASELINE-A");
        Assert.Equal(6500m, garage.StartingBalance);
        Assert.Equal(6500m, garage.StartingOverdueDebt);
        Assert.Equal(new DateOnly(2026, 8, 1), garage.AccountingStartMonth);
        Assert.Equal(new DateOnly(2026, 7, 1), garage.InitialMeterReadingMonth);
        Assert.Equal(4, await db.MeterDevices.CountAsync());
        Assert.Equal(committed ? 2 : 0, await db.MeterDevices.CountAsync(d => d.IsAccountingBaseline));
        Assert.Equal(committed ? 0m : 101m, await db.Funds.Where(f => f.Name == "Synthetic cleanup fund").Select(f => f.Balance).SingleAsync());
        Assert.False(File.Exists(Path.Combine(barrierDirectory, "maintenance-active")));
        Assert.False(File.Exists(Path.Combine(barrierDirectory, "recovery-required")));
        Assert.False(File.Exists(Path.Combine(barrierDirectory, "participants-resume-required")));
        Assert.False(File.Exists(Path.Combine(barrierDirectory, "operation-progress")));
        foreach (var timer in new[] { "garagebalance-healthcheck.timer", "garagebalance-storage-sync.timer" })
        {
            var state = await SystemctlAsync(["show", timer, "--property=ActiveState", "--all"], default);
            Assert.Contains("ActiveState=active", state.Output);
        }
    }

    private Task SignalAsync(string phase, CancellationToken token)
    {
        File.WriteAllText(Path.Combine(root, "phase"), phase);
        return Task.Delay(Timeout.InfiniteTimeSpan, token);
    }
    private static async Task WriteAsync<T>(string path, T value, CancellationToken token)
    {
        await using var output = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        await JsonSerializer.SerializeAsync(output, value, Json, token);
        await output.FlushAsync(token);
        output.Flush(flushToDisk: true);
    }
    private static async Task<T> ReadAsync<T>(string path) => JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), Json) ?? throw new InvalidOperationException("fixture_json_invalid");
    private static async Task NativeAsync(string command, string[] arguments)
    {
        if (command is not ("pg_dump" or "pg_restore")) throw new InvalidOperationException("fixture_native_invalid");
        var bin = Environment.GetEnvironmentVariable("GB_FIXTURE_PG_BIN") ?? "/usr/lib/postgresql/18/bin";
        if (!Regex.IsMatch(bin, @"\A/usr/lib/postgresql/[0-9]+/bin\z")) throw new InvalidOperationException("fixture_pg_bin_invalid");
        var result = await AccountingCleanupHostEnvironment.RunAsync(bin + "/" + command, arguments, default);
        if (result.ExitCode != 0) throw new InvalidOperationException("fixture_native_failed");
    }

    private sealed class PausingAudit(GarageBalanceDbContext db, Action pause) : IAuditEventWriter
    {
        public AuditEvent? Add(AuditEventWriteRequest request)
        {
            Assert.NotNull(db.Database.CurrentTransaction);
            pause();
            return new AuditEventWriter(db).Add(request);
        }
    }
}
