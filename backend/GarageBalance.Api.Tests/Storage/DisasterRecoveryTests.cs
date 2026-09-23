using System.Security.Cryptography;
using System.Text.Json;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Recovery;
using GarageBalance.Api.Infrastructure.Storage;
using GarageBalance.StorageTool;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GarageBalance.Api.Tests.Storage;

public sealed class DisasterRecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gb-recovery-test-" + Guid.NewGuid().ToString("N"));
    public DisasterRecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public void EncryptedBundleRestoresExistingProtectedSecretAndAuthenticatedInventory()
    {
        var ring = CreateKeyRing();
        var key = RandomNumberGenerator.GetBytes(32);
        var canary = RecoveryArchive.CreateCanary(ring);
        var payload = new RecoveryArchivePayload(2, "garagebalance", Guid.NewGuid(), DateTimeOffset.UtcNow, [], canary);
        var encrypted = RecoveryArchive.Protect(ring, payload, key);
        Assert.DoesNotContain(canary.ProtectedValue, System.Text.Encoding.UTF8.GetString(encrypted), StringComparison.Ordinal);
        var restored = RecoveryArchive.Unprotect(encrypted, key, Path.Combine(root, "recovered"));
        Assert.Equal(payload.BundleId, restored.BundleId);
        RecoveryArchive.VerifyCanary(Path.Combine(root, "recovered", "key-ring"), restored.Canary);
        var bootstrap = new RecoveryBootstrap(2, "garagebalance", payload.BundleId, payload.CreatedAtUtc, encrypted.Length, RecoveryArchive.Hash(encrypted), [new("remote-aa", "domain-aa", "object.gbrb")]);
        Assert.Equal(bootstrap.BundleId, RecoveryArchive.Verify(RecoveryArchive.Sign(bootstrap, key), key).BundleId);
    }

    [Fact]
    public void BundleRejectsWrongKeyTamperMissingKeysAndExistingOutput()
    {
        var ring = CreateKeyRing();
        var key = RandomNumberGenerator.GetBytes(32);
        var payload = new RecoveryArchivePayload(2, "garagebalance", Guid.NewGuid(), DateTimeOffset.UtcNow, [], RecoveryArchive.CreateCanary(ring));
        var encrypted = RecoveryArchive.Protect(ring, payload, key);
        Assert.Throws<AuthenticationTagMismatchException>(() => RecoveryArchive.Unprotect(encrypted, RandomNumberGenerator.GetBytes(32), Path.Combine(root, "wrong")));
        Assert.False(Directory.Exists(Path.Combine(root, "wrong")));
        encrypted[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => RecoveryArchive.Unprotect(encrypted, key, Path.Combine(root, "tampered")));
        Assert.Throws<IOException>(() => RecoveryArchive.Unprotect(encrypted, key, root));
        Assert.Throws<CryptographicException>(() => RecoveryArchive.VerifyCanary(CreateKeyRing("other"), payload.Canary));
    }

    [Fact]
    public void BootstrapRejectsTamperWrongKeyAndOversizedArtifact()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var descriptor = new RecoveryBootstrap(2, "garagebalance", Guid.NewGuid(), DateTimeOffset.UtcNow, 100, new string('a', 64), []);
        var signed = RecoveryArchive.Sign(descriptor, key);
        Assert.Throws<CryptographicException>(() => RecoveryArchive.Verify(signed, RandomNumberGenerator.GetBytes(32)));
        Assert.Throws<CryptographicException>(() => RecoveryArchive.Verify(signed with { Payload = signed.Payload + "a" }, key));
        Assert.Throws<InvalidDataException>(() => RecoveryArchive.Verify(RecoveryArchive.Sign(descriptor with { SizeBytes = long.MaxValue }, key), key));
        Assert.Throws<InvalidDataException>(() => RecoveryArchive.Sign(descriptor with { Copies = [new("remote-aa", "domain-a", new string('a', 128_000))] }, key));
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Database=garagebalance;Username=admin")]
    [InlineData("Host=192.168.1.1;Database=postgres;Username=admin")]
    [InlineData("")]
    public void DrillCannotConnectToProductionDatabaseOrRemoteCluster(string connection) =>
        Assert.Throws<InvalidOperationException>(() => RestoreDrill.ValidateAdminConnection(connection));

    [Fact]
    public void DrillRequiresMatchingGeneratedDatabaseAndOnlyLoopbackListener()
    {
        var run = Guid.NewGuid().ToString("N");
        var settings = new Dictionary<string, string?> { ["RecoveryDrill:RunId"] = run, ["ConnectionStrings:DefaultConnection"] = $"Host=127.0.0.1;Database=gb_restore_{run};Username=operator", ["urls"] = "http://127.0.0.1:5123" };
        RecoveryDrillSafety.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        settings["urls"] = "http://0.0.0.0:5123";
        Assert.Throws<InvalidOperationException>(() => RecoveryDrillSafety.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
        settings["urls"] = "http://127.0.0.1:5123";
        settings["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=garagebalance;Username=operator";
        Assert.Throws<InvalidOperationException>(() => RecoveryDrillSafety.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
        settings["ConnectionStrings:DefaultConnection"] = $"Host=127.0.0.1;Database=gb_restore_{run};Username=operator";
        settings["Kestrel:Endpoints:Another:Url"] = "http://0.0.0.0:80";
        Assert.Throws<InvalidOperationException>(() => RecoveryDrillSafety.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrillConfigurationRemovesOnlyApplicationHostedServicesWhenExplicitlyEnabled(bool enabled)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        var run = Guid.NewGuid().ToString("N");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RecoveryDrill:Enabled"] = enabled.ToString(),
            ["RecoveryDrill:RunId"] = run,
            ["ConnectionStrings:DefaultConnection"] = $"Host=127.0.0.1;Database=gb_restore_{run};Username=operator",
            ["urls"] = "http://127.0.0.1:5123"
        });
        var frameworkType = System.Reflection.Assembly.Load("Microsoft.AspNetCore.Hosting")
            .GetType("Microsoft.AspNetCore.Hosting.GenericWebHostService", throwOnError: true)!;
        Assert.True(typeof(IHostedService).IsAssignableFrom(frameworkType));
        var frameworkService = ServiceDescriptor.Singleton(typeof(IHostedService), frameworkType);
        var applicationService = ServiceDescriptor.Singleton<IHostedService, DatabaseStartupHostedService>();
        var adjacentApplicationService = ServiceDescriptor.Singleton<IHostedService, DatabaseStartupHostedService>();
        var ordinaryService = ServiceDescriptor.Singleton(typeof(DatabaseStartupOptions), typeof(DatabaseStartupOptions));
        builder.Services.Add(frameworkService);
        builder.Services.Add(applicationService);
        builder.Services.Add(adjacentApplicationService);
        builder.Services.Add(ordinaryService);

        Assert.Equal(enabled, RecoveryDrillSafety.Configure(builder));
        Assert.Contains(frameworkService, builder.Services);
        Assert.Contains(ordinaryService, builder.Services);
        Assert.Equal(!enabled, builder.Services.Contains(applicationService));
        Assert.Equal(!enabled, builder.Services.Contains(adjacentApplicationService));
    }

    [Theory]
    [InlineData("POST", "/api/auth/login", true)]
    [InlineData("GET", "/health", true)]
    [InlineData("GET", "/api/auth/me", true)]
    [InlineData("GET", "/api/reports/consolidated", true)]
    [InlineData("POST", "/api/settings/backups", false)]
    [InlineData("POST", "/api/auth/bootstrap-admin", false)]
    [InlineData("DELETE", "/api/dictionaries/garages/1", false)]
    [InlineData("GET", "/api/settings/integrations", false)]
    public void DrillHttpBoundaryDeniesAllUnneededRoutes(string method, string path, bool allowed) =>
        Assert.Equal(allowed, RecoveryDrillSafety.IsAllowedRequest(method, path));

    [Fact]
    public void SelectionExcludesPrimaryDeletedStaleCorruptAndDisabledCopies()
    {
        var config = Configuration();
        var good = new StorageManifestReplicaEntry("remote-aa", "independent-a", "backup", 1, StorageReplicaState.Available, 4, new string('a', 64), DateTimeOffset.UtcNow);
        var backup = new StorageManifestEntry(Guid.NewGuid(), Guid.NewGuid(), StorageDataClass.DatabaseBackup, "backup", 1, 4, new string('a', 64), StorageObjectState.Protected, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            [good, good with { DestinationId = "local-hot", FailureDomain = "primary" }, good with { Generation = 0 }, good with { State = StorageReplicaState.Corrupted }, good with { Sha256 = new string('b', 64) }]);
        var inventory = new RecoveryArchivePayload(2, "garagebalance", Guid.NewGuid(), DateTimeOffset.UtcNow, [backup, backup with { State = StorageObjectState.Deleted }], new("unused", "unused"));
        Assert.Single(RestoreDrill.SelectIndependentReplicas(inventory, config, "primary"));
        Assert.Empty(RestoreDrill.SelectIndependentReplicas(inventory, config with { Destinations = config.Destinations.Select(item => item with { State = StorageDestinationState.Disabled }).ToArray() }, "primary"));
    }

    [Fact]
    public void RecoveryPolicyRequiresTwoTrulyDifferentDomainsAndCapabilities()
    {
        var config = Configuration();
        var policy = new StoragePolicyOptions { DataClass = StorageDataClass.RecoverySecrets, RequiredIndependentCopies = 2 };
        DisasterRecoveryCommands.ValidateRecoveryTargets(policy, config.Destinations);
        Assert.Throws<InvalidOperationException>(() => DisasterRecoveryCommands.ValidateRecoveryTargets(policy, config.Destinations.Select(item => item with { FailureDomain = "same" }).ToArray()));
        Assert.Throws<InvalidOperationException>(() => DisasterRecoveryCommands.ValidateRecoveryTargets(policy, config.Destinations.Select(item => item with { Capabilities = StorageCapability.Read }).ToArray()));
        Assert.Throws<InvalidOperationException>(() => DisasterRecoveryCommands.ValidateRecoveryTargets(new StoragePolicyOptions { DataClass = StorageDataClass.RecoverySecrets, RequiredIndependentCopies = 1 }, config.Destinations));
    }

    [Fact]
    public async Task VerifiedRecoveryReadRejectsWrongHashWrongSizeAndCancellation()
    {
        var provider = new LocalFileStorageProvider("local-hot", root);
        await File.WriteAllBytesAsync(Path.Combine(root, "bundle"), [1, 2, 3, 4]);
        Assert.Equal([1, 2, 3, 4], await DisasterRecoveryCommands.ReadBoundedAsync(provider, "bundle", 4, RecoveryArchive.Hash([1, 2, 3, 4]), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => DisasterRecoveryCommands.ReadBoundedAsync(provider, "bundle", 3, new string('a', 64), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => DisasterRecoveryCommands.ReadBoundedAsync(provider, "bundle", 4, new string('a', 64), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DisasterRecoveryCommands.ReadBoundedAsync(provider, "bundle", 4, RecoveryArchive.Hash([1, 2, 3, 4]), new CancellationToken(true)));
    }

    [Fact]
    public async Task RecoveryFetchFallsBackFromCorruptBundleWithoutOpeningProductionDatabase()
    {
        var ring = CreateKeyRing();
        var key = RandomNumberGenerator.GetBytes(32);
        var payload = new RecoveryArchivePayload(2, "garagebalance", Guid.NewGuid(), DateTimeOffset.UtcNow, [], RecoveryArchive.CreateCanary(ring));
        var encrypted = RecoveryArchive.Protect(ring, payload, key);
        var primary = Path.Combine(root, "a");
        var secondary = Path.Combine(root, "b");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);
        await File.WriteAllBytesAsync(Path.Combine(primary, "bundle"), [0]);
        await File.WriteAllBytesAsync(Path.Combine(secondary, "bundle"), encrypted);
        var bootstrap = new RecoveryBootstrap(2, "garagebalance", payload.BundleId, payload.CreatedAtUtc, encrypted.Length, RecoveryArchive.Hash(encrypted),
            [new("remote-aa", "independent-a", "bundle"), new("remote-bb", "independent-b", "bundle")]);
        var bootstrapPath = Path.Combine(root, "bootstrap.json");
        var keyPath = Path.Combine(root, "key.txt");
        await File.WriteAllTextAsync(bootstrapPath, JsonSerializer.Serialize(RecoveryArchive.Sign(bootstrap, key), RecoveryArchive.Json));
        await File.WriteAllTextAsync(keyPath, Convert.ToBase64String(key));
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "not a connection",
            ["Storage:Destinations:0:Id"] = "remote-aa",
            ["Storage:Destinations:0:FailureDomain"] = "independent-a",
            ["Storage:Destinations:0:RootPath"] = primary,
            ["Storage:Destinations:1:Id"] = "remote-bb",
            ["Storage:Destinations:1:FailureDomain"] = "independent-b",
            ["Storage:Destinations:1:RootPath"] = secondary
        };
        foreach (var index in new[] { 0, 1 }) foreach (var (capability, capIndex) in new[] { "Read", "Write", "Stat" }.Select((value, i) => (value, i)))
            settings[$"Storage:Destinations:{index}:Capabilities:{capIndex}"] = capability;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var output = Path.Combine(root, "fetched");
        Assert.Equal(0, await DisasterRecoveryCommands.RunAsync(["recovery-fetch", "--execute", "--bootstrap", bootstrapPath, "--key-file", keyPath, "--output", output], configuration, CancellationToken.None));
        RecoveryArchive.VerifyCanary(Path.Combine(output, "key-ring"), payload.Canary);
        Assert.True(File.Exists(Path.Combine(output, "catalog.json")));
    }

    [Fact]
    public async Task CommandsAreDryRunGuardedBeforeAnyFilesOrConnections()
    {
        var configuration = new ConfigurationBuilder().Build();
        foreach (var command in new[] { "recovery-publish", "recovery-fetch", "recovery-canary", "restore-drill" })
        {
            Assert.True(DisasterRecoveryCommands.CanHandle(command));
            Assert.Equal(3, await DisasterRecoveryCommands.RunAsync([command], configuration, CancellationToken.None));
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    private string CreateKeyRing(string name = "keys")
    {
        var path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        _ = DataProtectionProvider.Create(new DirectoryInfo(path), options => options.SetApplicationName("GarageBalance")).CreateProtector("init").Protect("synthetic");
        return path;
    }

    private static EffectiveStorageConfiguration Configuration()
    {
        var options = new StorageOptions
        {
            Destinations = [new() { Id = "local-hot", FailureDomain = "primary", RootPath = "a", Capabilities = ["Read", "Write", "Stat"] }, new() { Id = "remote-aa", FailureDomain = "independent-a", RootPath = "b", Capabilities = ["Read", "Write", "Stat"] }],
            Pools = [new() { Id = "backups", DestinationIds = ["local-hot", "remote-aa"] }],
            Policies = [new() { Id = "backups", PoolId = "backups", DataClass = StorageDataClass.DatabaseBackup }]
        };
        return new StorageConfigurationResolver(Microsoft.Extensions.Options.Options.Create(options), Microsoft.Extensions.Options.Options.Create(new GarageBalance.Api.Application.Backups.DatabaseBackupOptions())).Resolve();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
