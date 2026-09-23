using System.Security.Cryptography;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GarageBalance.Api.Tests.Storage;

public sealed class PostgreSqlStorageMigrationTests
{
    [PostgreSqlFact]
    public async Task RealLegacyDump_BackfillsReplicatesVerifiesAndResumesWithoutClaimingDelete()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var root = Directory.CreateTempSubdirectory("garagebalance-pg-migration-").FullName;
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var mirrorRoot = Directory.CreateDirectory(Path.Combine(root, "mirror")).FullName;
            var filename = "garagebalance_manual_20260922_120000_001.pgdump";
            var dump = Path.Combine(localRoot, filename);
            var connection = new NpgsqlConnectionStringBuilder(database.ConnectionString);
            var locator = new BackupToolLocator();
            var commands = new BackupCommandRunner();
            var pgDump = locator.Resolve("pg_dump");
            Assert.NotNull(pgDump);
            var result = await commands.RunAsync(new(pgDump,
                ["--host", connection.Host!, "--port", connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                 "--username", connection.Username!, "--dbname", connection.Database!, "--format=custom", "--file", dump, "--no-password"],
                new Dictionary<string, string> { ["PGPASSWORD"] = connection.Password ?? "" }), CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            await using var context = database.CreateContext();
            var options = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations =
                [
                    new() { Id = "local-hot", RootPath = localRoot, Type = StorageProviderType.LocalFileSystem, FailureDomain = "local-host", Capabilities = ["Read", "Write", "Stat", "Delete"] },
                    new() { Id = "offsite-a", Type = StorageProviderType.S3Compatible, FailureDomain = "test-mirror-host", Endpoint = "https://s3.test", Bucket = "private-tests", Capabilities = ["Read", "Write", "Stat", "Delete"] }
                ],
                Pools = [new() { Id = "backup-pool", DestinationIds = ["local-hot", "offsite-a"] }],
                Policies = [new() { Id = "backup-policy", DataClass = StorageDataClass.DatabaseBackup, PoolId = "backup-pool", RequiredIndependentCopies = 2, MinimumOffsiteCopies = 1, DesiredCopies = 2 }]
            };
            var backupOptions = new DatabaseBackupOptions { Directory = localRoot };
            var resolver = new StorageConfigurationResolver(Options.Create(options), Options.Create(backupOptions));
            var catalog = new EfStorageCatalog(context, resolver);
            // An isolated filesystem stand-in exercises actual bytes; it is not real-S3 acceptance evidence.
            var registry = new TestRegistry(new LocalFileStorageProvider("local-hot", localRoot), new LocalFileStorageProvider("offsite-a", mirrorRoot));
            var runner = new StorageReplicationRunner(catalog, registry, resolver, TimeProvider.System,
                new StorageOperationHealthTracker(TimeProvider.System), NullLogger<StorageReplicationRunner>.Instance);
            var engine = new StorageMigrationEngine(catalog, registry, resolver.Resolve(), new LocalBackupInspector(commands, locator, "pg_restore"),
                (owner, kinds, ids, token) => runner.ProcessNextAsync(owner, token, kinds, ids), new StorageMaintenanceLock(context));
            var checkpoint = Path.Combine(root, "migration.json");
            MigrationCommandOptions Command(string command, bool execute = false) => new(command, execute, checkpoint, 2, 10, null);

            Assert.Equal(10, (await engine.ExecuteAsync(Command("cutover-check"), CancellationToken.None)).ExitCode);
            var copied = await engine.ExecuteAsync(Command("copy", true), CancellationToken.None);
            Assert.Equal(0, copied.ExitCode);
            Assert.Equal("copied_requires_verification", Assert.IsType<MigrationCheckpoint>(copied.Details).State);
            Assert.True(File.Exists(dump + ".manifest.json"));
            Assert.Equal(SHA256.HashData(await File.ReadAllBytesAsync(dump)), SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(mirrorRoot, filename))));
            Assert.Equal(0, (await engine.ExecuteAsync(Command("verify", true), CancellationToken.None)).ExitCode);
            Assert.Equal(0, (await engine.ExecuteAsync(Command("cutover-check"), CancellationToken.None)).ExitCode);
            Assert.Equal(0, (await engine.ExecuteAsync(Command("rollback-check"), CancellationToken.None)).ExitCode);
            await catalog.TombstoneAndScheduleDeleteAsync("garagebalance", StorageDataClass.DatabaseBackup, filename, 12, DateTimeOffset.UtcNow, CancellationToken.None);
            await engine.ExecuteAsync(Command("resume", true), CancellationToken.None);
            Assert.True(File.Exists(dump));
            Assert.True(File.Exists(Path.Combine(mirrorRoot, filename)));
            Assert.All(await context.StorageTransferJobs.Where(job => job.Kind == StorageTransferJobKind.Delete).ToArrayAsync(), job =>
                Assert.Equal(StorageTransferJobState.Ready, job.State));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class TestRegistry(params IStorageProvider[] providers) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) => providers.Single(item => item.DestinationId == destinationId);
        public IReadOnlyList<IStorageProvider> GetAll() => providers;
    }
}
