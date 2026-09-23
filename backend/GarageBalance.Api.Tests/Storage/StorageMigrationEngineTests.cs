using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageMigrationEngineTests
{
    [Theory]
    [InlineData("garagebalance_manual_20260922_120000_001.pgdump", true)]
    [InlineData("garagebalance_20260624-1130.pgdump", true)]
    [InlineData("garagebalance_20260624-164006.pgdump", true)]
    [InlineData("garagebalance_20260923-181024_e049429b56eb9b105ae325253f03878bd39b3cda-342.pgdump", true)]
    [InlineData("garagebalance_auth_reset_20260624-164543.pgdump", true)]
    [InlineData("garagebalance_before_access_transfer_v2_20260624-182214.pgdump", true)]
    [InlineData("garagebalance_before_manual_entry_20260714_141629.pgdump", true)]
    [InlineData("other_project_20260923.pgdump", false)]
    [InlineData("garagebalance_20260923-181024_untrusted.pgdump", false)]
    public void RecognizesOnlyKnownArchiveNameFormats(string name, bool expected) =>
        Assert.Equal(expected, LocalBackupInspector.IsManagedName(name));

    [Fact]
    public async Task InventoryBlocksUnknownPgDumpRatherThanSilentlySkippingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        File.WriteAllBytes(Path.Combine(fixture.Root, "other_project_20260923.pgdump"), [1, 2, 3, 4]);
        var plan = await fixture.Engine.ExecuteAsync(fixture.Command("plan"), CancellationToken.None);
        var blocked = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MigrationPlanItem>>(plan.Details));
        Assert.Equal("blocked", blocked.Action);
        Assert.Equal("invalid_name_or_link", blocked.Reason);
    }

    [Fact]
    public async Task LegacyInventory_RequiresTocAndSha_AndDryRunWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var file = await fixture.AddFileAsync(1);
        var inventory = await fixture.Engine.ExecuteAsync(fixture.Command("inventory"), CancellationToken.None);
        Assert.Equal(1, inventory.LocalFiles);
        Assert.Equal(0, inventory.CatalogObjects);
        var dry = await fixture.Engine.ExecuteAsync(fixture.Command("copy"), CancellationToken.None);
        Assert.True(dry.DryRun);
        Assert.False(File.Exists(file + ".manifest.json"));
        Assert.False(File.Exists(fixture.Checkpoint));
        Assert.Empty(await fixture.Db.Context.StorageObjects.ToArrayAsync());
        fixture.Commands.ExitCode = 1;
        var invalid = await fixture.Engine.ExecuteAsync(fixture.Command("plan"), CancellationToken.None);
        Assert.Equal("blocked", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<MigrationPlanItem>>(invalid.Details)).Action);
    }

    [Fact]
    public async Task Copy_RegistersLegacyWithoutRenaming_ResumeKeepsSnapshot_DeltaAddsNewFiles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddFileAsync(1);
        var copied = await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        var checkpoint = Assert.IsType<MigrationCheckpoint>(copied.Details);
        Assert.Equal(1, checkpoint.RegisteredCount);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(first + ".manifest.json"));
        var firstObject = await fixture.Db.Context.StorageObjects.SingleAsync();
        Assert.Equal(Path.GetFileName(first), firstObject.LogicalKey);
        var second = await fixture.AddFileAsync(2);
        await fixture.Engine.ExecuteAsync(fixture.Command("resume", true), CancellationToken.None);
        Assert.Single(await fixture.Db.Context.StorageObjects.ToArrayAsync());
        await fixture.Engine.ExecuteAsync(fixture.Command("delta-sync", true), CancellationToken.None);
        Assert.Equal(2, await fixture.Db.Context.StorageObjects.CountAsync());
        Assert.True(File.Exists(second + ".manifest.json"));
        Assert.All(fixture.ClaimKinds, kinds => Assert.DoesNotContain(StorageTransferJobKind.Delete, kinds));
    }

    [Fact]
    public async Task Resume_RejectsChangedSourceOrCheckpointTampering_AndReleasesLock()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        await File.AppendAllTextAsync(path, "changed");
        await Assert.ThrowsAsync<MigrationToolException>(() => fixture.Engine.ExecuteAsync(fixture.Command("resume", true), CancellationToken.None));
        await using (var released = new MigrationCheckpointStore(fixture.Checkpoint))
            await released.LockAsync(CancellationToken.None);
        var original = await File.ReadAllTextAsync(fixture.Checkpoint);
        await File.WriteAllTextAsync(fixture.Checkpoint, original.Replace("replicating", "tampered", StringComparison.Ordinal).Replace("pending", "damaged", StringComparison.Ordinal));
        await using var store = new MigrationCheckpointStore(fixture.Checkpoint);
        await Assert.ThrowsAsync<MigrationToolException>(() => store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EmptyOrUnregisteredInventory_CannotPassCoverageGate()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(10, (await fixture.Engine.ExecuteAsync(fixture.Command("cutover-check"), CancellationToken.None)).ExitCode);
        await fixture.AddFileAsync(1);
        var gate = await fixture.Engine.ExecuteAsync(fixture.Command("cutover-check"), CancellationToken.None);
        Assert.Contains("unregistered_or_invalid_local_archives", gate.Blockers);
    }

    [Fact]
    public async Task Gate_VerifiesBytesAndOffsite_AndRollbackRequiresCurrentFlatLocalCopy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var file = await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        var missingRemote = await fixture.Engine.ExecuteAsync(fixture.Command("cutover-check"), CancellationToken.None);
        Assert.Contains("required_physical_coverage_not_met", missingRemote.Blockers);
        Assert.Equal(0, (await fixture.Engine.ExecuteAsync(fixture.Command("rollback-check"), CancellationToken.None)).ExitCode);
        File.Delete(file);
        Assert.Equal(10, (await fixture.Engine.ExecuteAsync(fixture.Command("rollback-check"), CancellationToken.None)).ExitCode);
    }

    [Fact]
    public async Task Tombstone_IsNeverResurrected_AndOldBinaryRollbackIsBlockedWhileFileRemains()
    {
        await using var fixture = await Fixture.CreateAsync();
        var file = await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        await fixture.Catalog.TombstoneAndScheduleDeleteAsync("garagebalance", StorageDataClass.DatabaseBackup,
            Path.GetFileName(file), 12, DateTimeOffset.UtcNow, CancellationToken.None);
        var claimsBeforeResume = fixture.ClaimedObjects.Count;
        await fixture.Engine.ExecuteAsync(fixture.Command("resume", true), CancellationToken.None);
        Assert.Equal(StorageObjectState.Deleting, (await fixture.Db.Context.StorageObjects.SingleAsync()).State);
        Assert.True(File.Exists(file));
        Assert.Contains("tombstoned_local_files_would_reappear_in_old_binary",
            (await fixture.Engine.ExecuteAsync(fixture.Command("rollback-check"), CancellationToken.None)).Blockers);
        Assert.Equal(claimsBeforeResume, fixture.ClaimedObjects.Count);
    }

    [Fact]
    public async Task CatalogPagination_HasNoHistoricalCutoff()
    {
        await using var fixture = await Fixture.CreateAsync();
        for (var index = 0; index < 1101; index++)
            fixture.Db.Context.StorageObjects.Add(new StorageObject
            {
                OperationId = Guid.NewGuid(),
                LogicalKey = $"archive-{index:D6}",
                OriginalFileName = "archive.pgdump",
                DataClass = StorageDataClass.DatabaseBackup,
                PolicyId = "backup-policy",
                CommittedGeneration = 1,
                SizeBytes = 3,
                Sha256 = new string('a', 64),
                State = StorageObjectState.Protected
            });
        await fixture.Db.Context.SaveChangesAsync();
        Assert.Equal(1101, (await fixture.Engine.ReadCatalogAsync(100, CancellationToken.None)).Count);
    }

    [Theory]
    [InlineData("resume", "--execute")]
    [InlineData("copy", "--max-jobs", "0")]
    [InlineData("inventory", "--unknown", "value")]
    [InlineData("inventory", "--page-size", "1001")]
    public void InvalidArguments_AreRejected(params string[] arguments) =>
        Assert.Throws<MigrationToolException>(() => MigrationCommandOptions.Parse(arguments));

    [Fact]
    public async Task Cancellation_DoesNotCreateCheckpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddFileAsync(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Engine.ExecuteAsync(fixture.Command("copy", true), cancellation.Token));
        Assert.False(File.Exists(fixture.Checkpoint));
    }

    [Fact]
    public async Task PhysicalVerification_RejectsSameLengthCorruption_AndDoesNotExposeProviderMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        await File.WriteAllBytesAsync(path, [4, 3, 2, 1]);
        var verified = await fixture.Engine.ExecuteAsync(fixture.Command("verify"), CancellationToken.None);
        Assert.Equal(10, verified.ExitCode);
        var item = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<ObjectVerification>>(verified.Details));
        Assert.Equal("checksum_mismatch", item.Replicas.Single(replica => replica.DestinationId == "local-hot").Error);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(verified), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PausedSafetyGuard_BlocksRepairBeforeClaimingAnyJobs()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        await fixture.Guard.PauseAsync(11, CancellationToken.None);
        var claims = fixture.ClaimKinds.Count;
        await Assert.ThrowsAsync<MigrationToolException>(() => fixture.Engine.ExecuteAsync(fixture.Command("repair", true), CancellationToken.None));
        Assert.Equal(claims, fixture.ClaimKinds.Count);
        Assert.True((await fixture.Guard.GetStateAsync(CancellationToken.None)).Paused);
    }

    [Fact]
    public async Task CheckpointLock_IsExclusive_AndCancelledSaveKeepsLastValidCheckpoint()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        await using var first = new MigrationCheckpointStore(fixture.Checkpoint);
        await using var second = new MigrationCheckpointStore(fixture.Checkpoint);
        await first.LockAsync(CancellationToken.None);
        await Assert.ThrowsAsync<MigrationToolException>(() => second.LockAsync(CancellationToken.None));
        var original = await first.ReadAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.SaveAsync(original with { State = "must_not_commit" }, cancelled.Token));
        Assert.Equal(original.State, (await first.ReadAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task LostSidecarBackfill_PreservesExistingCatalogOperationIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var path = await fixture.AddFileAsync(1);
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true), CancellationToken.None);
        var existing = await fixture.Db.Context.StorageObjects.SingleAsync();
        var retainedIdentity = Guid.NewGuid();
        existing.OperationId = retainedIdentity;
        await fixture.Db.Context.SaveChangesAsync();
        File.Delete(path + ".manifest.json");
        await fixture.Engine.ExecuteAsync(fixture.Command("copy", true) with { Checkpoint = Path.Combine(fixture.Root, "second-checkpoint.json") }, CancellationToken.None);
        var sidecar = JsonSerializer.Deserialize<DatabaseBackupManifest>(await File.ReadAllTextAsync(path + ".manifest.json"), MigrationCheckpointStore.JsonOptions);
        Assert.Equal(retainedIdentity, sidecar?.BackupId);
        Assert.Single(await fixture.Db.Context.StorageObjects.ToArrayAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteTestDatabase Db { get; init; }
        public required string Root { get; init; }
        public required EfStorageCatalog Catalog { get; init; }
        public required StorageMigrationEngine Engine { get; set; }
        public required IStorageReconciliationGuard Guard { get; init; }
        public FakeCommands Commands { get; } = new();
        public string Checkpoint => Path.Combine(Root, "checkpoint.json");
        public List<StorageTransferJobKind[]> ClaimKinds { get; } = [];
        public List<Guid[]> ClaimedObjects { get; } = [];

        public MigrationCommandOptions Command(string command, bool execute = false) => new(command, execute, Checkpoint, 100, 10, null);
        public Task<string> AddFileAsync(int number)
        {
            var path = Path.Combine(Root, $"garagebalance_manual_20260922_120000_{number:D3}.pgdump");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            return Task.FromResult(path);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("garagebalance-migration-tests-").FullName;
            var db = await SqliteTestDatabase.CreateAsync();
            var config = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations =
                [
                    new() { Id = "local-hot", RootPath = root, Type = StorageProviderType.LocalFileSystem, FailureDomain = "local-host", Capabilities = ["Read", "Write", "Stat", "Delete"] },
                    new() { Id = "offsite-a", Type = StorageProviderType.S3Compatible, FailureDomain = "remote-host", Endpoint = "https://s3.test", Bucket = "private-backups", Capabilities = ["Read", "Write", "Stat", "Delete"] }
                ],
                Pools = [new() { Id = "backup-pool", DestinationIds = ["local-hot", "offsite-a"] }],
                Policies = [new() { Id = "backup-policy", DataClass = StorageDataClass.DatabaseBackup, PoolId = "backup-pool", RequiredIndependentCopies = 2, MinimumOffsiteCopies = 1, DesiredCopies = 2 }]
            };
            var resolver = new StorageConfigurationResolver(Options.Create(config), Options.Create(new DatabaseBackupOptions { Directory = root }));
            var catalog = new EfStorageCatalog(db.Context, resolver);
            var guard = new FileStorageReconciliationGuard(resolver, TimeProvider.System);
            var fixture = new Fixture { Db = db, Root = root, Catalog = catalog, Engine = null!, Guard = guard };
            fixture.Engine = new(catalog, new FakeRegistry(new LocalFileStorageProvider("local-hot", root)), resolver.Resolve(),
                new LocalBackupInspector(fixture.Commands, new FakeLocator(), "pg_restore"),
                (_, kinds, ids, _) => { fixture.ClaimKinds.Add(kinds.ToArray()); fixture.ClaimedObjects.Add(ids.ToArray()); return Task.FromResult(false); },
                new StorageMaintenanceLock(db.Context), guard);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeCommands : IBackupCommandRunner
    {
        public int ExitCode { get; set; }
        public Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.Equal("--list", command.Arguments[0]); return Task.FromResult(new BackupCommandResult(ExitCode, "")); }
    }
    private sealed class FakeLocator : IBackupToolLocator { public string? Resolve(string configuredPath) => configuredPath; }
    private sealed class FakeRegistry(IStorageProvider local) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) => destinationId == local.DestinationId ? local : throw new StorageProviderException(StorageErrorCategory.ObjectMissing, "missing");
        public IReadOnlyList<IStorageProvider> GetAll() => [local];
    }
}
