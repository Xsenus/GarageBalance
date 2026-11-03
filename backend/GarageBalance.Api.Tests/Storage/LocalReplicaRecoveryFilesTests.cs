using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Backups;
using GarageBalance.StorageTool;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class LocalReplicaRecoveryFilesTests
{
    [Fact]
    public async Task PublishesOnlyExactMissingArchivesAndAnExplicitRetryVerifiesExistingCopies()
    {
        using var f = new Fixture();
        var result = await f.Run();
        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.AlreadyPresent);
        Assert.Equal(8, result.Bytes);
        Assert.All(f.Plan.Items, item => Assert.Equal(f.Bytes, File.ReadAllBytes(Path.Combine(f.Root, item.FileName))));
        Assert.Equal(f.OriginalBytes, File.ReadAllBytes(f.Original));
        Assert.Equal(2, Directory.GetFiles(f.Staging, "*.pgdump").Length);
        Assert.Empty(Directory.GetFiles(f.Root, "*.tmp"));
        var retry = await f.Run();
        Assert.Equal(0, retry.Created);
        Assert.Equal(2, retry.AlreadyPresent);
        Assert.All(f.Plan.Items, item => Assert.False(File.Exists(Path.Combine(f.Root, item.FileName + ".manifest.json"))));
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("hash")]
    [InlineData("config")]
    [InlineData("catalog")]
    [InlineData("tombstone")]
    [InlineData("generation")]
    [InlineData("source")]
    [InlineData("identity")]
    [InlineData("date")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    [InlineData("inside-root")]
    [InlineData("same-root")]
    [InlineData("original-drift")]
    [InlineData("staging-corrupt")]
    [InlineData("native-invalid")]
    [InlineData("existing-conflict")]
    [InlineData("disk")]
    public async Task RejectsDriftAndUnreviewedScopeWithoutTouchingAnyTarget(string failure)
    {
        using var f = new Fixture();
        var item = f.Plan.Items[0];
        var confirmation = f.Plan.Fingerprint;
        switch (failure)
        {
            case "confirm": confirmation = new string('b', 64); break;
            case "hash": f.Plan = f.Plan with { Fingerprint = new string('b', 64) }; break;
            case "config": f.Plan = f.Rehash(f.Plan with { ConfigurationHash = new string('b', 64) }); confirmation = f.Plan.Fingerprint; break;
            case "catalog": f.Manifest[0] = f.Manifest[0] with { UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) }; break;
            case "tombstone": f.Manifest[0] = f.Manifest[0] with { State = StorageObjectState.Deleted }; f.RefreshCatalog(); confirmation = f.Plan.Fingerprint; break;
            case "generation": f.Plan = f.Rehash(f.Plan with { Items = [item with { Generation = 2 }, f.Plan.Items[1]] }); confirmation = f.Plan.Fingerprint; break;
            case "source": f.Plan = f.Rehash(f.Plan with { Items = [item with { VerifiedSources = [] }, f.Plan.Items[1]] }); confirmation = f.Plan.Fingerprint; break;
            case "identity": f.Plan = f.Rehash(f.Plan with { Items = [item with { OperationId = Guid.NewGuid() }, f.Plan.Items[1]] }); confirmation = f.Plan.Fingerprint; break;
            case "date": f.Plan = f.Rehash(f.Plan with { Items = [item with { CreatedAtUtc = item.CreatedAtUtc.AddDays(1) }, f.Plan.Items[1]] }); confirmation = f.Plan.Fingerprint; break;
            case "duplicate": f.Plan = f.Rehash(f.Plan with { Items = [item, item] }); confirmation = f.Plan.Fingerprint; break;
            case "traversal": f.Plan = f.Rehash(f.Plan with { Items = [item with { FileName = "../escape.pgdump" }, f.Plan.Items[1]] }); confirmation = f.Plan.Fingerprint; break;
            case "inside-root": f.Plan = f.Rehash(f.Plan with { StagingDirectory = Directory.CreateDirectory(Path.Combine(f.Root, "inside")).FullName }); confirmation = f.Plan.Fingerprint; break;
            case "same-root": f.Plan = f.Rehash(f.Plan with { StagingDirectory = f.Root }); confirmation = f.Plan.Fingerprint; break;
            case "original-drift": File.WriteAllBytes(f.Original, [7, 7, 7]); break;
            case "staging-corrupt": File.WriteAllBytes(Path.Combine(f.Staging, item.FileName), [7, 7, 7]); break;
            case "native-invalid": f.Commands.ExitCode = 1; break;
            case "existing-conflict": File.WriteAllBytes(Path.Combine(f.Root, item.FileName), [7, 7, 7]); break;
            case "disk": f.FreeBytes = 0; break;
        }
        var before = Directory.GetFiles(f.Root, "*.pgdump").ToDictionary(path => new FileInfo(path).Name, File.ReadAllBytes);
        await Assert.ThrowsAsync<MigrationToolException>(() => f.Run(confirmation));
        var after = Directory.GetFiles(f.Root, "*.pgdump");
        Assert.Equal(before.Count, after.Length);
        foreach (var path in after) Assert.Equal(before[Path.GetFileName(path)], File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(f.Root, "*.tmp"));
    }

    [Fact]
    public async Task PermissionFailureAndCancellationPublishNothing()
    {
        using var f = new Fixture();
        f.Authority = _ => throw new InvalidOperationException("Synthetic missing authority");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.Single(Directory.GetFiles(f.Root, "*.pgdump"));
        f.Authority = token => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Run(token: cancelled.Token));
        Assert.Single(Directory.GetFiles(f.Root, "*.pgdump"));
    }

    [Fact]
    public async Task PartialPublicationIsRetainedAndFreshValidationCanCompleteTheRemainingFiles()
    {
        using var f = new Fixture();
        var calls = 0;
        f.Authority = _ => { if (++calls == 4) throw new IOException("Synthetic interruption"); return Task.CompletedTask; };
        await Assert.ThrowsAsync<IOException>(() => f.Run());
        Assert.Equal(2, Directory.GetFiles(f.Root, "*.pgdump").Length);
        Assert.Empty(Directory.GetFiles(f.Root, "*.tmp"));
        f.Authority = _ => Task.CompletedTask;
        var result = await f.Run();
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.AlreadyPresent);
        Assert.Equal(f.OriginalBytes, File.ReadAllBytes(f.Original));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string folder = Directory.CreateTempSubdirectory("gb-local-promotion-tests-").FullName;
        public string Root { get; }
        public string Staging { get; }
        public string Original { get; }
        public byte[] Bytes { get; } = [1, 2, 3, 4];
        public byte[] OriginalBytes { get; } = [5, 6, 7, 8];
        public EffectiveStorageConfiguration Config { get; }
        public LocalReplicaRecoveryPlan Plan { get; set; }
        public List<StorageManifestEntry> Manifest { get; } = [];
        public Commands Commands { get; } = new();
        public Func<CancellationToken, Task> Authority { get; set; } = _ => Task.CompletedTask;
        public long FreeBytes { get; set; } = long.MaxValue;
        public Fixture()
        {
            Root = Directory.CreateDirectory(Path.Combine(folder, "working")).FullName;
            Staging = Directory.CreateDirectory(Path.Combine(folder, "private")).FullName;
            Original = Path.Combine(Root, "garagebalance_20261021-090000.pgdump");
            File.WriteAllBytes(Original, OriginalBytes);
            var options = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations = [new() { Id = "local", RootPath = Root, FailureDomain = "local", Capabilities = ["Read", "Stat", "Write"] },
                    new() { Id = "offsite", Type = StorageProviderType.S3Compatible, Endpoint = "https://s3.test", Bucket = "backup", FailureDomain = "offsite", Capabilities = ["Read", "Stat"] }],
                Pools = [new() { Id = "backups", DestinationIds = ["local", "offsite"] }],
                Policies = [new() { Id = "backup-policy", PoolId = "backups", DataClass = StorageDataClass.DatabaseBackup, DesiredCopies = 2, RequiredIndependentCopies = 2, MinimumOffsiteCopies = 1 }]
            };
            Config = new StorageConfigurationResolver(Options.Create(options), Options.Create(new DatabaseBackupOptions { Directory = Root })).Resolve();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Bytes));
            for (var i = 1; i <= 2; i++)
            {
                var name = $"garagebalance_20261021-10000{i}.pgdump";
                File.WriteAllBytes(Path.Combine(Staging, name), Bytes);
                Manifest.Add(new(Guid.NewGuid(), Guid.NewGuid(), StorageDataClass.DatabaseBackup, name, 1, 4, hash,
                    StorageObjectState.Protected, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow,
                    new[] { "local", "offsite" }.Select(id => new StorageManifestReplicaEntry(id, id, name, 1, StorageReplicaState.Available, 4, hash, DateTimeOffset.UtcNow)).ToArray()));
            }
            var inventory = Inspector().InspectAsync(Root, Config.TenantId, Config.Policies[0], default).GetAwaiter().GetResult();
            Plan = Rehash(new(1, Guid.NewGuid(), DateTimeOffset.UtcNow, LocalReplicaRecoveryStager.ConfigurationHash(Config),
                LocalReplicaRecoveryStager.CatalogHash(Manifest), LocalReplicaRecoveryStager.InventoryHash(inventory), "local", Root, Staging,
                Manifest.Select(e => new LocalRecoveryItem(e.ObjectId, e.OperationId, e.LogicalKey, e.Generation, e.SizeBytes, e.Sha256, e.CreatedAtUtc,
                    [new("offsite", "offsite", e.LogicalKey)])).ToArray(), ""));
        }
        public LocalReplicaRecoveryPlan Rehash(LocalReplicaRecoveryPlan plan) => plan with { Fingerprint = LocalReplicaRecoveryStager.Fingerprint(plan) };
        public void RefreshCatalog() => Plan = Rehash(Plan with { CatalogHash = LocalReplicaRecoveryStager.CatalogHash(Manifest) });
        private LocalBackupInspector Inspector() => new(Commands, new Locator(), "pg_restore");
        public Task<LocalRecoveryPromotionResult> Run(string? confirmation = null, CancellationToken token = default) =>
            new LocalReplicaRecoveryFiles(Config, Inspector(), Authority, OperatingSystem.IsLinux() ? checked((int)getgid()) : null, _ => FreeBytes)
                .PromoteAsync(Plan, confirmation ?? Plan.Fingerprint, Manifest, token);
        public void Dispose() => Directory.Delete(folder, recursive: true);
        [DllImport("libc")] private static extern uint getgid();
    }
    private sealed class Commands : IBackupCommandRunner
    {
        public int ExitCode { get; set; }
        public Task<BackupCommandResult> RunAsync(BackupCommand command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new BackupCommandResult(ExitCode, "")); }
    }
    private sealed class Locator : IBackupToolLocator { public string? Resolve(string configuredPath) => configuredPath; }
}
