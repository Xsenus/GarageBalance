using System.Security.Cryptography;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageReadRouterTests
{
    [Fact]
    public async Task MissingPreferredReplica_FallsBackToVerifiedCurrentReplica()
    {
        byte[] bytes = "backup"u8.ToArray();
        var storageObject = CreateObject(bytes);
        var preferred = new MemoryProvider("local-hot");
        var fallback = new MemoryProvider("offsite-a");
        fallback.Seed("remote/current", bytes);
        var router = CreateRouter(storageObject, preferred, fallback);

        var result = await router.OpenByLogicalKeyAsync(
            "garagebalance",
            StorageDataClass.DatabaseBackup,
            storageObject.LogicalKey,
            CancellationToken.None);

        Assert.Equal("offsite-a", result.DestinationId);
        await using var content = result.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal(1, preferred.StatCalls);
        Assert.Equal(1, fallback.StatCalls);
    }

    [Fact]
    public async Task StaleOrCorruptReplica_IsNeverSelectedEvenWhenItIsFirst()
    {
        byte[] bytes = "current"u8.ToArray();
        var storageObject = CreateObject(bytes);
        storageObject.Replicas[0].Generation = 0;
        var stale = new MemoryProvider("local-hot");
        stale.Seed("local/current", "stale"u8.ToArray());
        var current = new MemoryProvider("offsite-a");
        current.Seed("remote/current", bytes);
        var router = CreateRouter(storageObject, stale, current);

        var result = await router.OpenByLogicalKeyAsync("garagebalance", StorageDataClass.DatabaseBackup, storageObject.LogicalKey, CancellationToken.None);

        Assert.Equal("offsite-a", result.DestinationId);
        Assert.Equal(0, stale.StatCalls);
        await result.Content.DisposeAsync();
    }

    [Fact]
    public async Task TombstonedObject_IsUnavailableWithoutProviderAttempt()
    {
        var storageObject = CreateObject("backup"u8.ToArray());
        storageObject.TombstonedAtUtc = DateTimeOffset.UtcNow;
        storageObject.State = StorageObjectState.Deleting;
        var preferred = new MemoryProvider("local-hot");
        var fallback = new MemoryProvider("offsite-a");
        var router = CreateRouter(storageObject, preferred, fallback);

        var error = await Assert.ThrowsAsync<StorageProviderException>(() =>
            router.OpenByLogicalKeyAsync("garagebalance", StorageDataClass.DatabaseBackup, storageObject.LogicalKey, CancellationToken.None));

        Assert.Equal(StorageErrorCategory.ObjectMissing, error.Category);
        Assert.Equal(0, preferred.StatCalls + fallback.StatCalls);
    }

    private static StorageObject CreateObject(byte[] bytes)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var storageObject = new StorageObject
        {
            Id = Guid.NewGuid(),
            TenantId = "garagebalance",
            DataClass = StorageDataClass.DatabaseBackup,
            LogicalKey = "database/current.pgdump",
            CommittedGeneration = 1,
            SizeBytes = bytes.Length,
            Sha256 = sha256,
            State = StorageObjectState.Protected
        };
        storageObject.Replicas.Add(new StorageObjectReplica
        {
            DestinationId = "local-hot",
            NativeLocator = "local/current",
            FailureDomain = "local-host",
            Generation = 1,
            State = StorageReplicaState.Available,
            SizeBytes = bytes.Length,
            Sha256 = sha256
        });
        storageObject.Replicas.Add(new StorageObjectReplica
        {
            DestinationId = "offsite-a",
            NativeLocator = "remote/current",
            FailureDomain = "host-a",
            Generation = 1,
            State = StorageReplicaState.Available,
            SizeBytes = bytes.Length,
            Sha256 = sha256
        });
        return storageObject;
    }

    private static StorageReadRouter CreateRouter(StorageObject storageObject, params MemoryProvider[] providers)
    {
        var options = new StorageOptions
        {
            Mode = StorageMode.AsyncMirror,
            Destinations = providers.Select(provider => new StorageDestinationOptions
            {
                Id = provider.DestinationId,
                Type = StorageProviderType.LocalFileSystem,
                FailureDomain = provider.DestinationId,
                RootPath = "unused",
                Capabilities = ["Read", "Write", "Stat", "Delete"]
            }).ToList()
        };
        var resolver = new StorageConfigurationResolver(
            Options.Create(options),
            Options.Create(new DatabaseBackupOptions { Directory = "unused" }));
        return new StorageReadRouter(
            new ReadCatalog(storageObject),
            new ReadRegistry(providers),
            resolver,
            new StorageOperationHealthTracker(TimeProvider.System),
            NullLogger<StorageReadRouter>.Instance);
    }

    private sealed class ReadCatalog(StorageObject storageObject) : IStorageCatalog
    {
        public Task<StorageObject?> FindByLogicalKeyAsync(string tenantId, StorageDataClass dataClass, string logicalKey, CancellationToken cancellationToken) => Task.FromResult<StorageObject?>(storageObject);
        public Task<StorageObject?> FindObjectAsync(Guid objectId, CancellationToken cancellationToken) => Task.FromResult<StorageObject?>(storageObject);
        public Task<StorageCatalogRegistration> RegisterCommittedObjectAsync(RegisterCommittedStorageObjectRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageTransferJob?> ClaimNextJobAsync(string leaseOwner, TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageTransferContext> GetLeasedJobContextAsync(Guid jobId, string leaseOwner, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReplicationUploadingAsync(Guid jobId, string leaseOwner, string nativeLocator, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteReplicationAsync(Guid jobId, string leaseOwner, string nativeLocator, string? providerVersionId, string? providerChecksum, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleReplicationRetryAsync(Guid jobId, string leaseOwner, DateTimeOffset dueAtUtc, StorageReplicaState replicaState, string category, string safeError, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BlockReplicationAsync(Guid jobId, string leaseOwner, string category, string safeError, int requiredCopies, int desiredCopies, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ScheduleRepairAsync(Guid objectId, string destinationId, StorageReplicaState observedState, string category, string safeError, int requiredCopies, int desiredCopies, int maximumAttempts, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageObject?> TombstoneAndScheduleDeleteAsync(string tenantId, StorageDataClass dataClass, string logicalKey, int maximumAttempts, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteReplicaDeleteAsync(Guid jobId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleDeleteRetryAsync(Guid jobId, string leaseOwner, DateTimeOffset dueAtUtc, string category, string safeError, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteJobAsync(Guid jobId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleJobRetryAsync(Guid jobId, string leaseOwner, DateTimeOffset dueAtUtc, string category, string safeError, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageManifestEntry>> ExportManifestAsync(StorageDataClass dataClass, int take, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadRegistry(IEnumerable<MemoryProvider> providers) : IStorageProviderRegistry
    {
        private readonly Dictionary<string, IStorageProvider> items = providers.ToDictionary(item => item.DestinationId, item => (IStorageProvider)item);
        public IStorageProvider GetRequired(string destinationId) => items[destinationId];
        public IReadOnlyList<IStorageProvider> GetAll() => items.Values.ToArray();
    }

    private sealed class MemoryProvider(string destinationId) : IStorageProvider
    {
        private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);
        public string DestinationId { get; } = destinationId;
        public StorageCapability Capabilities => StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat;
        public int StatCalls { get; private set; }
        public void Seed(string locator, byte[] bytes) => objects[locator] = bytes;
        public string GetWriteLocator(StorageWriteRequest request) => request.ObjectKey;
        public Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(objects[nativeLocator], writable: false));
        public Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            StatCalls++;
            return Task.FromResult(objects.TryGetValue(nativeLocator, out var bytes)
                ? new StorageObjectStat(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), null, new Dictionary<string, string>())
                : null);
        }
        public Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageDownloadLink?> GetDownloadLinkAsync(string nativeLocator, TimeSpan lifetime, CancellationToken cancellationToken) => Task.FromResult<StorageDownloadLink?>(null);
    }
}
