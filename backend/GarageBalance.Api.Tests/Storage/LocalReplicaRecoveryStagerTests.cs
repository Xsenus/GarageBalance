using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.StorageTool;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class LocalReplicaRecoveryStagerTests
{
    [Fact]
    public async Task DryRunDoesNotReadCloudOrWriteAnythingAndSkipsTombstones()
    {
        using var f = new Fixture();
        f.Manifest.Add(f.Entry with { ObjectId = Guid.NewGuid(), LogicalKey = "garagebalance_20261021-100001.pgdump", State = StorageObjectState.Deleted });
        var result = await f.Run(false);
        Assert.True(result.DryRun && result.StagingOnly);
        Assert.Equal(1, result.Files);
        Assert.Equal(4, result.RequiredBytes);
        Assert.Empty(Directory.GetFileSystemEntries(f.Private));
        Assert.All(f.Remotes, r => Assert.Equal(0, r.Reads));
        Assert.Empty(Directory.GetFileSystemEntries(f.Root));
    }

    [Fact]
    public async Task ExecuteRechecksBothIndependentCloudCopiesAndNativeArchiveThenSealsPlan()
    {
        using var f = new Fixture();
        var result = await f.Run();
        Assert.False(result.DryRun);
        var plan = JsonSerializer.Deserialize<LocalReplicaRecoveryPlan>(await File.ReadAllTextAsync(f.Plan), MigrationCheckpointStore.JsonOptions)!;
        Assert.Equal(result.Fingerprint, LocalReplicaRecoveryStager.Fingerprint(plan));
        Assert.Equal(LocalReplicaRecoveryStager.ConfigurationHash(f.Configuration), plan.ConfigurationHash);
        Assert.Equal(LocalReplicaRecoveryStager.CatalogHash(f.Manifest), plan.CatalogHash);
        Assert.Equal(LocalReplicaRecoveryStager.InventoryHash([]), plan.InventoryHash);
        Assert.Equal(2, Assert.Single(plan.Items).VerifiedSources.Count);
        Assert.All(f.Remotes, r => Assert.Equal(1, r.Reads));
        Assert.Equal(f.Bytes, await File.ReadAllBytesAsync(Path.Combine(plan.StagingDirectory, f.Entry.LogicalKey)));
        Assert.Equal(1, f.Commands.Calls);
        Assert.Empty(Directory.GetFileSystemEntries(f.Root));
        Assert.Equal(1, f.CatalogReads);
        Assert.Equal(1, f.InventoryReads);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("missing")]
    [InlineData("stat")]
    [InlineData("provider")]
    [InlineData("io")]
    [InlineData("deadline")]
    [InlineData("capability")]
    [InlineData("toc")]
    [InlineData("catalog-drift")]
    [InlineData("inventory-drift")]
    public async Task FailedProofLeavesWorkingRootUntouchedAndRemovesOwnedStaging(string failure)
    {
        using var f = new Fixture();
        f.Remotes[1].Failure = failure;
        f.Commands.ExitCode = failure == "toc" ? 1 : 0;
        f.CatalogDrift = failure == "catalog-drift";
        f.InventoryDrift = failure == "inventory-drift";
        await Assert.ThrowsAsync<MigrationToolException>(() => f.Run());
        Assert.Empty(Directory.GetFileSystemEntries(f.Private));
        Assert.Empty(Directory.GetFileSystemEntries(f.Root));
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-key")]
    [InlineData("state")]
    [InlineData("generation")]
    [InlineData("hash")]
    [InlineData("name")]
    [InlineData("local-generation")]
    [InlineData("remote-generation")]
    [InlineData("same-domain")]
    [InlineData("local-conflict")]
    [InlineData("inventory-unverified")]
    [InlineData("inventory-disagrees")]
    [InlineData("no-space")]
    [InlineData("no-plan")]
    [InlineData("relative")]
    [InlineData("inside-root")]
    [InlineData("overwrite")]
    [InlineData("command")]
    public async Task RejectsUnreviewedStateBeforeDownloading(string failure)
    {
        using var f = new Fixture();
        var entry = f.Entry;
        switch (failure)
        {
            case "duplicate-id": f.Manifest.Add(entry with { LogicalKey = "garagebalance_20261021-100001.pgdump" }); break;
            case "duplicate-key": f.Manifest.Add(entry with { ObjectId = Guid.NewGuid() }); break;
            case "state": f.Manifest[0] = entry with { State = StorageObjectState.ProtectionPending }; break;
            case "generation": f.Manifest[0] = entry with { Generation = 0 }; break;
            case "hash": f.Manifest[0] = entry with { Sha256 = "bad" }; break;
            case "name": f.Manifest[0] = entry with { LogicalKey = "unreviewed.pgdump" }; break;
            case "local-generation": f.Manifest[0] = entry with { Replicas = entry.Replicas.Select(r => r.DestinationId == "local" ? r with { Generation = 2 } : r).ToArray() }; break;
            case "remote-generation": f.Manifest[0] = entry with { Replicas = entry.Replicas.Select(r => r.DestinationId != "local" ? r with { Generation = 2 } : r).ToArray() }; break;
            case "same-domain": f.Manifest[0] = entry with { Replicas = entry.Replicas.Select(r => r.DestinationId == "remote-b" ? r with { FailureDomain = "remote-a" } : r).ToArray() }; break;
            case "local-conflict": File.WriteAllBytes(Path.Combine(f.Root, entry.LogicalKey), [9]); break;
            case "inventory-unverified": f.Inventory.Add(new(entry.LogicalKey, 4, entry.Sha256, false, true, "invalid", null)); break;
            case "inventory-disagrees": f.Inventory.Add(new(entry.LogicalKey, 4, entry.Sha256, true, true, null, null)); break;
            case "no-space": f.FreeBytes = 1; break;
            case "no-plan": f.Plan = null!; break;
            case "relative": f.Plan = "relative.json"; break;
            case "inside-root": f.Plan = Path.Combine(f.Root, "plan.json"); break;
            case "overwrite": File.WriteAllText(f.Plan, "existing"); break;
        }
        await Assert.ThrowsAsync<MigrationToolException>(() => f.Run(command: failure == "command" ? "repair" : "local-recovery-stage"));
        Assert.All(f.Remotes, r => Assert.Equal(0, r.Reads));
        if (failure == "overwrite") Assert.Equal("existing", File.ReadAllText(f.Plan));
    }

    [Fact]
    public async Task ExistingVerifiedLocalCopyIsPreservedAndCancellationPropagates()
    {
        using var f = new Fixture();
        File.WriteAllBytes(Path.Combine(f.Root, f.Entry.LogicalKey), f.Bytes);
        f.Inventory.Add(new(f.Entry.LogicalKey, 4, f.Entry.Sha256, true, true, null, null));
        Assert.Equal(0, (await f.Run(false)).Files);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Run(token: cancelled.Token));
        Assert.Equal(f.Bytes, File.ReadAllBytes(Path.Combine(f.Root, f.Entry.LogicalKey)));
        Assert.Throws<MigrationToolException>(() => MigrationCommandOptions.Parse(["local-recovery-stage", "--execute"]));
        Assert.False(MigrationCommandOptions.Parse(["local-recovery-stage"]).Execute);
        Assert.True(MigrationCommandOptions.Parse(["local-recovery-stage", "--execute", "--checkpoint", f.Plan]).Execute);
        Assert.Equal(LocalReplicaRecoveryStager.CatalogHash(f.Manifest), LocalReplicaRecoveryStager.CatalogHash([f.Entry with { Replicas = f.Entry.Replicas.Reverse().ToArray() }]));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("gb-local-stage-tests-").FullName;
        public string Root { get; }
        public string Private { get; }
        public string Plan { get; set; }
        public byte[] Bytes { get; } = [1, 2, 3, 4];
        public EffectiveStorageConfiguration Configuration { get; }
        public List<StorageManifestEntry> Manifest { get; } = [];
        public StorageManifestEntry Entry => Manifest[0];
        public List<LocalBackupInventory> Inventory { get; } = [];
        public Remote[] Remotes { get; }
        public Commands Commands { get; } = new();
        public long FreeBytes { get; set; } = long.MaxValue;
        public bool CatalogDrift { get; set; }
        public bool InventoryDrift { get; set; }
        public int CatalogReads { get; private set; }
        public int InventoryReads { get; private set; }
        public Fixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(directory, "root")).FullName;
            Private = Directory.CreateDirectory(Path.Combine(directory, "private")).FullName;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Private, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Plan = Path.Combine(Private, "plan.json");
            var options = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations = [new() { Id = "local", RootPath = Root, FailureDomain = "local", Capabilities = ["Read", "Stat"] },
                    new() { Id = "remote-a", Type = StorageProviderType.S3Compatible, FailureDomain = "remote-a", Endpoint = "https://s3.test", Bucket = "backup", Capabilities = ["Read", "Stat"] },
                    new() { Id = "remote-b", Type = StorageProviderType.S3Compatible, FailureDomain = "remote-b", Endpoint = "https://s3.test", Bucket = "backup", Capabilities = ["Read", "Stat"] }],
                Pools = [new() { Id = "backups", DestinationIds = ["local", "remote-a", "remote-b"] }],
                Policies = [new() { Id = "backup-policy", PoolId = "backups", DataClass = StorageDataClass.DatabaseBackup, DesiredCopies = 3, RequiredIndependentCopies = 3, MinimumOffsiteCopies = 2 }]
            };
            Configuration = new StorageConfigurationResolver(Options.Create(options), Options.Create(new DatabaseBackupOptions { Directory = Root })).Resolve();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Bytes));
            var name = "garagebalance_20261021-100000.pgdump";
            Manifest.Add(new(Guid.NewGuid(), Guid.NewGuid(), StorageDataClass.DatabaseBackup, name, 1, 4, hash, StorageObjectState.Protected,
                DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow, new[] { "local", "remote-a", "remote-b" }.Select(id =>
                    new StorageManifestReplicaEntry(id, id, name, 1, StorageReplicaState.Available, 4, hash, DateTimeOffset.UtcNow)).ToArray()));
            Remotes = [new("remote-a", Bytes), new("remote-b", Bytes)];
        }
        public Task<LocalRecoveryStagingReport> Run(bool execute = true, string command = "local-recovery-stage", CancellationToken token = default)
        {
            var registry = new Registry([new LocalFileStorageProvider("local", Root), .. Remotes]);
            return new LocalReplicaRecoveryStager(registry, Configuration, new(Commands, new Locator(), "pg_restore"), TimeProvider.System, _ => FreeBytes)
                .ExecuteAsync(new(command, execute, Plan, 100, 10, null), Manifest, Inventory,
                    _ => { CatalogReads++; return Task.FromResult<IReadOnlyList<StorageManifestEntry>>(CatalogDrift ? [] : Manifest); },
                    _ => { InventoryReads++; return Task.FromResult<IReadOnlyList<LocalBackupInventory>>(InventoryDrift ? [new("drift", 1, null, false, true, null, null)] : Inventory); }, token);
        }
        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
    private sealed class Commands : IBackupCommandRunner
    {
        public int ExitCode { get; set; }
        public int Calls { get; private set; }
        public Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; Assert.Equal("--list", command.Arguments[0]); return Task.FromResult(new BackupCommandResult(ExitCode, "")); }
    }
    private sealed class Locator : IBackupToolLocator { public string? Resolve(string configuredPath) => configuredPath; }
    private sealed class Registry(IStorageProvider[] providers) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) => providers.Single(p => p.DestinationId == destinationId);
        public IReadOnlyList<IStorageProvider> GetAll() => providers;
    }
    private sealed class Remote(string id, byte[] bytes) : IStorageProvider
    {
        public string Failure { get; set; } = "";
        public int Reads { get; private set; }
        public string DestinationId => id;
        public StorageCapability Capabilities => Failure == "capability" ? StorageCapability.None : StorageCapability.Read | StorageCapability.Stat;
        public string GetWriteLocator(StorageWriteRequest request) => throw new InvalidOperationException("No writes allowed");
        public Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken cancellationToken) => throw new InvalidOperationException("No writes allowed");
        public Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken) => throw new InvalidOperationException("No deletes allowed");
        public Task<StorageDownloadLink?> GetDownloadLinkAsync(string nativeLocator, TimeSpan lifetime, CancellationToken cancellationToken) => throw new InvalidOperationException("No links allowed");
        public Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken) =>
            Task.FromResult<StorageObjectStat?>(Failure == "missing" ? null : new(Failure == "stat" ? 1 : bytes.Length, null, null, new Dictionary<string, string>()));
        public Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (Failure == "provider") throw new StorageProviderException(StorageErrorCategory.ProviderForbidden, "private message must not escape");
            if (Failure == "io") throw new IOException("private message must not escape");
            if (Failure == "deadline") throw new OperationCanceledException();
            return Task.FromResult<Stream>(new MemoryStream(Failure switch { "checksum" => [9, 9, 9, 9], "short" => [1], "long" => [1, 2, 3, 4, 5], _ => bytes }));
        }
    }
}
