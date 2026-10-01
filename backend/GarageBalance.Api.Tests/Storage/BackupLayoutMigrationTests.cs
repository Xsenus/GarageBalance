using System.Security.Cryptography;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Storage;

public sealed class BackupLayoutMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingOriginCannotResumeOrPruneAnOldLayoutPlan(bool prune)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RunAsync(true);
        var changed = fixture.Configuration with
        {
            Destinations = fixture.Configuration.Destinations.Select(destination =>
                destination.Id == "remote-a" ? destination with { BackupSource = "another/server" } : destination).ToArray()
        };
        var engine = new BackupLayoutMigration(fixture.Catalog, new Registry(fixture.Cloud, fixture.Local), changed, fixture.Lock);
        var error = await Assert.ThrowsAsync<MigrationToolException>(() => engine.ExecuteAsync(fixture.Options(true, prune), CancellationToken.None));
        Assert.Contains("configuration changed", error.Message);
        Assert.Equal(0, fixture.Cloud.Deletes);
        Assert.True(fixture.Cloud.Objects.ContainsKey(fixture.Old));
    }

    [Fact]
    public async Task RestoredOldCatalogCanRebindAlreadyMovedCloudCopyWithoutOldObject()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RunAsync(true);
        await fixture.RunAsync(true, true);
        var key = fixture.Cloud.Objects.Keys.Single();
        Assert.True(await fixture.Catalog.RelocateReplicaAsync(fixture.Object.Id, "remote-a", 1, fixture.Object.Sha256, fixture.Object.SizeBytes,
            key, new(fixture.Old, null, fixture.Object.Sha256), DateTimeOffset.UtcNow, CancellationToken.None));
        File.Delete(fixture.Checkpoint);
        Assert.Equal(1, (await fixture.RunAsync(true)).CatalogCommitted);
        Assert.Single(fixture.Cloud.Objects);
        Assert.Equal(1, (await fixture.RunAsync(true, true)).OldDeleted);
    }

    [Fact]
    public async Task DryRunThenCopyResumeAndPrunePreserveLogicalIdentityAndLocalArchive()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dry = await fixture.RunAsync(false);
        Assert.True(dry.DryRun);
        Assert.Equal(1, dry.Planned);
        Assert.False(File.Exists(fixture.Checkpoint));
        var copied = await fixture.RunAsync(true);
        Assert.Equal(1, copied.CatalogCommitted);
        Assert.Equal(0, copied.OldDeleted);
        Assert.Equal(2, fixture.Cloud.Objects.Count);
        var resumed = await fixture.RunAsync(true);
        Assert.Equal(1, resumed.CatalogCommitted);
        Assert.Equal(2, fixture.Cloud.Objects.Count);
        var pruned = await fixture.RunAsync(true, true);
        Assert.Equal(1, pruned.OldDeleted);
        Assert.Single(fixture.Cloud.Objects);
        Assert.Single(fixture.Local.Objects);
        Assert.Equal(1, (await fixture.RunAsync(true, true)).OldDeleted);
        var item = await fixture.Catalog.FindObjectAsync(fixture.Object.Id, CancellationToken.None);
        Assert.Equal("original.pgdump", item!.LogicalKey);
        Assert.Equal(fixture.Object.OperationId, item.OperationId);
        Assert.Equal(fixture.Object.Sha256, item.Sha256);
        Assert.StartsWith("backups/09_2026/sgk_28092026_143059_", item.Replicas.Single(replica => replica.DestinationId == "remote-a").NativeLocator);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("write")]
    [InlineData("replacement")]
    public async Task CopyFailureNeverChangesCatalogOrDeletesSource(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (failure == "source") fixture.Cloud.Objects[fixture.Old] = [9];
        if (failure == "write") fixture.Cloud.FailWrite = true;
        if (failure == "replacement") fixture.Cloud.CorruptWrite = true;
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(true));
        Assert.True(fixture.Cloud.Objects.ContainsKey(fixture.Old));
        Assert.Equal(fixture.Old, (await fixture.Catalog.FindObjectAsync(fixture.Object.Id, CancellationToken.None))!.Replicas.Single(replica => replica.DestinationId == "remote-a").NativeLocator);
        Assert.Equal(0, fixture.Cloud.Deletes);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("cloud")]
    [InlineData("checkpoint")]
    [InlineData("tombstone")]
    [InlineData("recovery")]
    public async Task PruneRequiresVerifiedCopiesLiveCatalogAndIntactCheckpoint(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RunAsync(true);
        if (failure == "local") fixture.Local.Objects["original.pgdump"] = [9];
        if (failure == "cloud") fixture.Cloud.Objects[fixture.Cloud.Objects.Keys.Single(key => key != fixture.Old)] = [9];
        if (failure == "checkpoint") await File.WriteAllTextAsync(fixture.Checkpoint, "{}");
        if (failure == "tombstone") await fixture.Catalog.TombstoneAndScheduleDeleteAsync("garagebalance", StorageDataClass.DatabaseBackup, "original.pgdump", 12, DateTimeOffset.UtcNow, CancellationToken.None);
        if (failure == "recovery")
        {
            fixture.Db.Context.StorageObjects.Add(new StorageObject { OperationId = Guid.NewGuid(), LogicalKey = "recovery", PolicyId = "backups-policy", DataClass = StorageDataClass.RecoverySecrets });
            await fixture.Db.Context.SaveChangesAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.RunAsync(true, true));
        Assert.Equal(0, fixture.Cloud.Deletes);
        Assert.True(fixture.Cloud.Objects.ContainsKey(fixture.Old));
    }

    [Fact]
    public async Task CancelBusyAndPruneWithoutCheckpointWriteNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<MigrationToolException>(() => fixture.RunAsync(true, true));
        fixture.Lock.Busy = true;
        await Assert.ThrowsAsync<MigrationToolException>(() => fixture.RunAsync(true));
        fixture.Lock.Busy = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Engine.ExecuteAsync(fixture.Options(true), cancellation.Token));
        Assert.False(File.Exists(fixture.Checkpoint));
        Assert.Equal(0, fixture.Cloud.Deletes);
    }

    [Fact]
    public async Task CatalogRelocationRejectsStaleGenerationHashSizeLocatorAndTerminalParent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var item = fixture.Object;
        var result = new StorageWriteResult("backups/new.pgdump", null, item.Sha256);
        async Task<bool> Move(long generation, string hash, long size, string old) => await fixture.Catalog.RelocateReplicaAsync(item.Id, "remote-a", generation, hash, size, old, result, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.False(await Move(2, item.Sha256, item.SizeBytes, fixture.Old));
        Assert.False(await Move(1, new string('b', 64), item.SizeBytes, fixture.Old));
        Assert.False(await Move(1, item.Sha256, item.SizeBytes + 1, fixture.Old));
        Assert.False(await Move(1, item.Sha256, item.SizeBytes, "stale"));
        Assert.True(await Move(1, item.Sha256, item.SizeBytes, fixture.Old));
        Assert.False(await Move(1, item.Sha256, item.SizeBytes, fixture.Old));
        await fixture.Catalog.TombstoneAndScheduleDeleteAsync("garagebalance", StorageDataClass.DatabaseBackup, item.LogicalKey, 12, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.False(await Move(1, item.Sha256, item.SizeBytes, result.NativeLocator));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteTestDatabase Db { get; init; }
        public required EfStorageCatalog Catalog { get; init; }
        public required StorageObject Object { get; init; }
        public required BackupLayoutMigration Engine { get; init; }
        public required EffectiveStorageConfiguration Configuration { get; init; }
        public required string Root { get; init; }
        public required string Old { get; init; }
        public required MemoryProvider Cloud { get; init; }
        public required MemoryProvider Local { get; init; }
        public required FakeLock Lock { get; init; }
        public string Checkpoint => Path.Combine(Root, "layout.json");
        public MigrationCommandOptions Options(bool execute, bool prune = false) => new(prune ? "backup-layout-prune" : "backup-layout", execute, Checkpoint, 200, 100, null);
        public Task<BackupLayoutReport> RunAsync(bool execute, bool prune = false) => Engine.ExecuteAsync(Options(execute, prune), CancellationToken.None);
        public static async Task<Fixture> CreateAsync()
        {
            var db = await SqliteTestDatabase.CreateAsync();
            var root = Directory.CreateTempSubdirectory("garagebalance-layout-").FullName;
            byte[] bytes = [1, 2, 3, 4];
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var item = new StorageObject
            {
                OperationId = Guid.NewGuid(),
                LogicalKey = "original.pgdump",
                OriginalFileName = "original.pgdump",
                PolicyId = "backups-policy",
                CommittedGeneration = 1,
                State = StorageObjectState.Protected,
                SizeBytes = bytes.Length,
                Sha256 = hash,
                CreatedAtUtc = new DateTimeOffset(2026, 9, 28, 7, 30, 59, TimeSpan.Zero)
            };
            var old = $"legacy/{item.OperationId:N}/g00000000000000000001/original.pgdump";
            item.Replicas.Add(new StorageObjectReplica { DestinationId = "local-hot", FailureDomain = "local-host", NativeLocator = "original.pgdump", Generation = 1, State = StorageReplicaState.Available, SizeBytes = bytes.Length, Sha256 = hash });
            item.Replicas.Add(new StorageObjectReplica { DestinationId = "remote-a", FailureDomain = "remote-host", NativeLocator = old, Generation = 1, State = StorageReplicaState.Available, SizeBytes = bytes.Length, Sha256 = hash });
            db.Context.StorageObjects.Add(item);
            await db.Context.SaveChangesAsync();
            var remote = new EffectiveStorageDestination("remote-a", StorageProviderType.S3Compatible, StorageDestinationState.Enabled, "remote-host", "garagebalance", null, new Uri("https://s3.test"), "bucket", "legacy", "us-east-1", true, 64000000, 16000000, "DefaultChain", StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat | StorageCapability.Delete, BackupPrefix: "backups");
            var local = remote with { Id = "local-hot", Type = StorageProviderType.LocalFileSystem, RootPath = root };
            var config = new EffectiveStorageConfiguration(StorageMode.AsyncMirror, "garagebalance", [local, remote],
                [new() { Id = "backups-pool", DestinationIds = ["local-hot", "remote-a"] }],
                [new() { Id = "backups-policy", DataClass = StorageDataClass.DatabaseBackup, PoolId = "backups-pool", RequiredIndependentCopies = 2, DesiredCopies = 2, MinimumOffsiteCopies = 1 }], new());
            var cloud = new MemoryProvider(remote);
            cloud.Objects.Add(old, bytes);
            var localProvider = new MemoryProvider(local);
            localProvider.Objects.Add("original.pgdump", bytes);
            var catalog = new EfStorageCatalog(db.Context);
            var maintenance = new FakeLock();
            return new()
            {
                Db = db,
                Root = root,
                Object = item,
                Old = old,
                Cloud = cloud,
                Local = localProvider,
                Catalog = catalog,
                Lock = maintenance,
                Configuration = config,
                Engine = new(catalog, new Registry(cloud, localProvider), config, maintenance)
            };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); Directory.Delete(Root, true); }
    }
    private sealed class FakeLock : IStorageMaintenanceLock, IAsyncDisposable
    {
        public bool Busy { get; set; }
        public Task<IAsyncDisposable?> TryAcquireAsync(string scope, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<IAsyncDisposable?>(Busy ? null : this); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Registry(params IStorageProvider[] providers) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) => providers.Single(provider => provider.DestinationId == destinationId);
        public IReadOnlyList<IStorageProvider> GetAll() => providers;
    }
    private sealed class MemoryProvider(EffectiveStorageDestination destination) : IStorageProvider
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public bool FailWrite { get; set; }
        public bool CorruptWrite { get; set; }
        public int Deletes { get; private set; }
        public string DestinationId => destination.Id;
        public StorageCapability Capabilities => destination.Capabilities;
        public string GetWriteLocator(StorageWriteRequest request) => ReadableBackupLayout.TryBuildKey(destination, request)!;
        public async Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken token)
        {
            if (FailWrite) throw new IOException("synthetic failure");
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, token);
            var key = GetWriteLocator(request);
            Objects.TryAdd(key, CorruptWrite ? [9] : buffer.ToArray());
            return new(key, null, request.Sha256);
        }
        public Task<Stream> OpenReadAsync(string locator, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(Objects[locator])); }
        public Task<StorageObjectStat?> StatAsync(string locator, CancellationToken token) => Task.FromResult(Objects.TryGetValue(locator, out var bytes) ? new StorageObjectStat(bytes.Length, null, null, new Dictionary<string, string>()) : null);
        public Task DeleteAsync(string locator, CancellationToken token) { Deletes++; Objects.Remove(locator); return Task.CompletedTask; }
        public Task<StorageDownloadLink?> GetDownloadLinkAsync(string locator, TimeSpan lifetime, CancellationToken token) => Task.FromResult<StorageDownloadLink?>(null);
    }
}
