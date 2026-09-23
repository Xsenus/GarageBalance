using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GarageBalance.Api.Tests.Storage;

public sealed class PostgreSqlDisasterRecoveryTests
{
    [PostgreSqlFact]
    public async Task IndependentReplicaAndEncryptedKeysRestoreIntoDisposableDatabaseAndRealApi()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "gb-dr-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var local = Path.Combine(root, "local");
            var remoteA = Path.Combine(root, "remote-a");
            var remoteB = Path.Combine(root, "remote-b");
            var ring = Path.Combine(root, "keys");
            var keyDirectory = Path.Combine(root, "independent-key");
            foreach (var path in new[] { local, remoteA, remoteB, ring, keyDirectory }) Directory.CreateDirectory(path);
            _ = DataProtectionProvider.Create(new DirectoryInfo(ring), options => options.SetApplicationName("GarageBalance")).CreateProtector("existing").Protect("synthetic");
            var keyFile = Path.Combine(keyDirectory, "encryption.txt");
            await File.WriteAllTextAsync(keyFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var canaryFile = Path.Combine(root, "canary.json");
            await File.WriteAllTextAsync(canaryFile, JsonSerializer.Serialize(RecoveryArchive.CreateCanary(ring), RecoveryArchive.Json));
            var dumpName = "garagebalance_manual_20260922_120000.pgdump";
            var dumpPath = Path.Combine(local, dumpName);
            await DumpAsync(database.ConnectionString, dumpPath);
            var storage = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations = [Destination("local-hot", "primary-host", local), Destination("remote-aa", "independent-aa", remoteA), Destination("remote-bb", "independent-bb", remoteB)],
                Pools = [new() { Id = "database-backups", DestinationIds = ["local-hot", "remote-aa", "remote-bb"] }, new() { Id = "recovery-secrets", DestinationIds = ["remote-aa", "remote-bb"] }],
                Policies = [new() { Id = "database-backups", PoolId = "database-backups", DataClass = StorageDataClass.DatabaseBackup, RequiredIndependentCopies = 2, DesiredCopies = 3 },
                    new() { Id = "recovery-secrets", PoolId = "recovery-secrets", DataClass = StorageDataClass.RecoverySecrets, RequiredIndependentCopies = 2, DesiredCopies = 2 }]
            };
            Assert.True(new StorageOptionsValidator().Validate(null, storage).Succeeded);
            var resolver = new StorageConfigurationResolver(Options.Create(storage), Options.Create(new DatabaseBackupOptions()));
            using var providers = new StorageProviderRegistry(resolver, new AwsS3ObjectClientFactory(), TimeProvider.System);
            await using (var db = database.CreateContext())
            {
                var catalog = new EfStorageCatalog(db);
                await using var dump = File.OpenRead(dumpPath);
                var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(dump));
                await catalog.RegisterCommittedObjectAsync(new(Guid.NewGuid(), "garagebalance", StorageDataClass.DatabaseBackup, dumpName, "database-backups", 1, 1,
                    dump.Length, sha, dumpName, "application/octet-stream", "local-hot", "primary-host", dumpName,
                    [new("remote-aa", "independent-aa"), new("remote-bb", "independent-bb")], 12, DateTimeOffset.UtcNow), CancellationToken.None);
                var runner = new StorageReplicationRunner(catalog, providers, resolver, TimeProvider.System, new StorageOperationHealthTracker(TimeProvider.System), NullLogger<StorageReplicationRunner>.Instance);
                Assert.True(await runner.ProcessNextAsync("dr-test", CancellationToken.None));
                Assert.True(await runner.ProcessNextAsync("dr-test", CancellationToken.None));
                Assert.Equal(3, await db.StorageObjectReplicas.CountAsync(item => item.State == StorageReplicaState.Available));
            }
            var bootstrap = Path.Combine(root, "operators", "bootstrap.json");
            var reportPath = Path.Combine(root, "operators", "verification.json");
            var admin = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = "postgres", Pooling = false };
            var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { Storage = storage })))
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                    ["Recovery:DrillConnectionString"] = admin.ConnectionString,
                    ["Storage:Recovery:VerificationReportPath"] = reportPath,
                    ["Storage:Recovery:MaximumDrillSeconds"] = "180"
                }).Build();
            Assert.Equal(0, await DisasterRecoveryCommands.RunAsync(["recovery-publish", "--execute", "--key-file", keyFile, "--key-ring", ring, "--canary", canaryFile, "--bootstrap", bootstrap], configuration, CancellationToken.None));
            var key = RecoveryArchive.ReadKey(keyFile);
            var signed = JsonSerializer.Deserialize<SignedRecoveryBootstrap>(await File.ReadAllTextAsync(bootstrap), RecoveryArchive.Json)!;
            var bootstrapData = RecoveryArchive.Verify(signed, key);
            Assert.Equal(2, bootstrapData.Copies.Count);
            CryptographicOperations.ZeroMemory(key);
            // Prove recovery does not need source files, source key ring or production DB access.
            File.Delete(dumpPath);
            Directory.Delete(ring, recursive: true);
            configuration["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=unused";
            // Also prove independent bundle failover: corrupt A, leaving B authoritative.
            var firstBundle = bootstrapData.Copies.Single(item => item.DestinationId == "remote-aa");
            await File.WriteAllBytesAsync(Path.Combine(remoteA, firstBundle.NativeLocator.Replace('/', Path.DirectorySeparatorChar)), [0]);
            var secondBundle = bootstrapData.Copies.Single(item => item.DestinationId == "remote-bb");
            var inspectionKey = RecoveryArchive.ReadKey(keyFile);
            var inspection = RecoveryArchive.Unprotect(await File.ReadAllBytesAsync(Path.Combine(remoteB, secondBundle.NativeLocator.Replace('/', Path.DirectorySeparatorChar))), inspectionKey, Path.Combine(root, "inspection"));
            Assert.Single(inspection.Backups);
            Assert.NotNull(inspection.Environment);
            Assert.Equal(2, inspection.Environment.Storage.Policies.Count);
            Assert.NotEmpty(inspection.Environment.AppliedMigrations);
            Assert.DoesNotContain(database.ConnectionString, JsonSerializer.Serialize(inspection.Environment), StringComparison.Ordinal);
            CryptographicOperations.ZeroMemory(inspectionKey);
            var apiDll = Path.Combine(FindRoot(), "backend", "GarageBalance.Api", "bin", "Release", "net10.0", "GarageBalance.Api.dll");
            var code = await DisasterRecoveryCommands.RunAsync(["restore-drill", "--execute", "--key-file", keyFile, "--bootstrap", bootstrap, "--exclude-failure-domain", "primary-host", "--api-dll", apiDll], configuration, CancellationToken.None);
            var report = JsonSerializer.Deserialize<RestoreVerificationReport>(await File.ReadAllTextAsync(reportPath), RecoveryArchive.Json)!;
            Assert.True(code == 0, $"Isolated drill failed at {report.ErrorCode}");
            Assert.True(report.Succeeded);
            Assert.NotNull(report.CompletedAtUtc);
            Assert.NotNull(report.BackupCreatedAtUtc);
            Assert.True(report.RtoSeconds > 0);
            Assert.Contains(report.SourceDestinationId, new[] { "remote-aa", "remote-bb" });
            Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), $"garagebalance-drill-{report.RunId:N}")));
            await using var check = new NpgsqlConnection(admin.ConnectionString);
            await check.OpenAsync();
            await using var checkDatabase = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", check);
            checkDatabase.Parameters.AddWithValue("name", $"gb_restore_{report.RunId:N}");
            Assert.Equal(0L, await checkDatabase.ExecuteScalarAsync());
            // Failed publication must retain the last known good bootstrap, not replace it with zero copies.
            var lastGoodBootstrap = await File.ReadAllTextAsync(bootstrap);
            configuration["ConnectionStrings:DefaultConnection"] = database.ConnectionString;
            var blockedRoot = Path.Combine(root, "blocked-file");
            await File.WriteAllTextAsync(blockedRoot, "not a directory");
            configuration["Storage:Destinations:1:RootPath"] = blockedRoot;
            configuration["Storage:Destinations:2:RootPath"] = blockedRoot;
            Assert.Equal(21, await DisasterRecoveryCommands.RunAsync(["recovery-publish", "--execute", "--key-file", keyFile, "--key-ring", Path.Combine(root, "inspection", "key-ring"), "--canary", canaryFile, "--bootstrap", bootstrap], configuration, CancellationToken.None));
            Assert.Equal(lastGoodBootstrap, await File.ReadAllTextAsync(bootstrap));
            Assert.True(File.Exists(bootstrap + ".pending.json"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static StorageDestinationOptions Destination(string id, string domain, string path) =>
        new() { Id = id, FailureDomain = domain, RootPath = path, Capabilities = ["Read", "Write", "Stat", "Delete"] };

    private static async Task DumpAsync(string connection, string output)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection);
        var start = new ProcessStartInfo("pg_dump") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { $"--host={settings.Host}", $"--port={settings.Port}", $"--username={settings.Username}", $"--dbname={settings.Database}", "--format=custom", "--no-owner", "--no-privileges", "--no-password", $"--file={output}" }) start.ArgumentList.Add(argument);
        start.Environment["PGPASSWORD"] = settings.Password ?? string.Empty;
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdout, stderr);
            Assert.Equal(0, process.ExitCode);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
