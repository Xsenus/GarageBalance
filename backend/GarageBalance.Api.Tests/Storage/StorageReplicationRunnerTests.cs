using System.Security.Cryptography;
using GarageBalance.Api.Application.Backups;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Storage;

public sealed class StorageReplicationRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DestinationFailure_DoesNotBlockAnotherDestinationAndProtectionBecomesSatisfied()
    {
        await using var fixture = await ReplicationFixture.CreateAsync();
        fixture.OffsiteA.WriteFailure = new StorageProviderException(
            StorageErrorCategory.TransientNetwork,
            "native endpoint details must not persist");

        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));
        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));

        await using var verification = fixture.CreateContext();
        var jobs = await verification.StorageTransferJobs.OrderBy(item => item.DestinationId).ToArrayAsync();
        Assert.Equal(StorageTransferJobState.RetryScheduled, jobs[0].State);
        Assert.Equal("TransientNetwork", jobs[0].LastErrorCategory);
        Assert.DoesNotContain("native endpoint", jobs[0].LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StorageTransferJobState.Completed, jobs[1].State);
        var storageObject = await verification.StorageObjects.Include(item => item.Replicas).SingleAsync();
        Assert.Equal(StorageObjectState.Protected, storageObject.State);
        Assert.Equal(StorageReplicaState.Unknown, storageObject.Replicas.Single(item => item.DestinationId == "offsite-a").State);
        Assert.Equal(StorageReplicaState.Available, storageObject.Replicas.Single(item => item.DestinationId == "offsite-b").State);
        Assert.Equal(1, fixture.OffsiteB.WriteCalls);
    }

    [Fact]
    public async Task LostWriteResponse_IsMarkedUnknownAndReconciledWithoutSecondUpload()
    {
        await using var fixture = await ReplicationFixture.CreateAsync(includeSecondOffsite: false);
        fixture.OffsiteA.LoseResponseAfterWrite = true;

        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));
        await using (var firstVerification = fixture.CreateContext())
        {
            var replica = await firstVerification.StorageObjectReplicas.SingleAsync(item => item.DestinationId == "offsite-a");
            var job = await firstVerification.StorageTransferJobs.SingleAsync();
            Assert.Equal(StorageReplicaState.Unknown, replica.State);
            Assert.Equal(StorageTransferJobState.RetryScheduled, job.State);
        }

        fixture.Clock.Advance(TimeSpan.FromMinutes(20));
        fixture.OffsiteA.LoseResponseAfterWrite = false;
        Assert.True(await fixture.Runner.ProcessNextAsync("worker-after-restart", CancellationToken.None));

        await using var verification = fixture.CreateContext();
        Assert.Equal(StorageTransferJobState.Completed, (await verification.StorageTransferJobs.SingleAsync()).State);
        Assert.Equal(StorageReplicaState.Available, (await verification.StorageObjectReplicas.SingleAsync(item => item.DestinationId == "offsite-a")).State);
        Assert.Equal(StorageObjectState.Protected, (await verification.StorageObjects.SingleAsync()).State);
        Assert.Equal(1, fixture.OffsiteA.WriteCalls);
    }

    [Fact]
    public async Task ImmutableConflict_IsBlockedAndDoesNotOverwriteRemoteBytes()
    {
        await using var fixture = await ReplicationFixture.CreateAsync(includeSecondOffsite: false);
        fixture.OffsiteA.Seed(fixture.ExpectedRemoteLocator("offsite-a"), "different"u8.ToArray());

        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));

        await using var verification = fixture.CreateContext();
        var job = await verification.StorageTransferJobs.SingleAsync();
        var replica = await verification.StorageObjectReplicas.SingleAsync(item => item.DestinationId == "offsite-a");
        Assert.Equal(StorageTransferJobState.Blocked, job.State);
        Assert.Equal("Conflict", job.LastErrorCategory);
        Assert.Equal(StorageReplicaState.Failed, replica.State);
        Assert.Equal(0, fixture.OffsiteA.WriteCalls);
    }

    [Fact]
    public async Task AllRemoteDestinationsUnavailable_KeepLocalCopyAndDurableIndependentDebts()
    {
        await using var fixture = await ReplicationFixture.CreateAsync();
        fixture.OffsiteA.WriteFailure = new StorageProviderException(StorageErrorCategory.TransientNetwork, "a down");
        fixture.OffsiteB.WriteFailure = new StorageProviderException(StorageErrorCategory.QuotaOrReadOnly, "b full");

        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));
        Assert.True(await fixture.Runner.ProcessNextAsync("worker-a", CancellationToken.None));

        await using var verification = fixture.CreateContext();
        var jobs = await verification.StorageTransferJobs.ToArrayAsync();
        Assert.All(jobs, job => Assert.Equal(StorageTransferJobState.RetryScheduled, job.State));
        Assert.Equal(2, jobs.Select(job => job.DestinationId).Distinct(StringComparer.Ordinal).Count());
        var storageObject = await verification.StorageObjects.Include(item => item.Replicas).SingleAsync();
        Assert.Equal(StorageObjectState.ProtectionDegraded, storageObject.State);
        Assert.Equal(StorageReplicaState.Available, storageObject.Replicas.Single(item => item.DestinationId == "local-hot").State);
    }

    private sealed class ReplicationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<GarageBalanceDbContext> dbOptions;
        private readonly GarageBalanceDbContext runnerContext;
        private readonly Guid operationId;
        private readonly string logicalKey;

        private ReplicationFixture(
            SqliteConnection connection,
            DbContextOptions<GarageBalanceDbContext> dbOptions,
            Guid operationId,
            string logicalKey,
            GarageBalanceDbContext runnerContext,
            MutableTimeProvider clock,
            FakeStorageProvider local,
            FakeStorageProvider offsiteA,
            FakeStorageProvider offsiteB,
            StorageReplicationRunner runner)
        {
            this.connection = connection;
            this.dbOptions = dbOptions;
            this.operationId = operationId;
            this.logicalKey = logicalKey;
            this.runnerContext = runnerContext;
            Clock = clock;
            Local = local;
            OffsiteA = offsiteA;
            OffsiteB = offsiteB;
            Runner = runner;
        }

        public MutableTimeProvider Clock { get; }
        public FakeStorageProvider Local { get; }
        public FakeStorageProvider OffsiteA { get; }
        public FakeStorageProvider OffsiteB { get; }
        public StorageReplicationRunner Runner { get; }

        public static async Task<ReplicationFixture> CreateAsync(bool includeSecondOffsite = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options;
            await using (var setupContext = new GarageBalanceDbContext(dbOptions))
            {
                await setupContext.Database.EnsureCreatedAsync();
            }

            byte[] bytes = "verified database backup bytes"u8.ToArray();
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var operationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            const string logicalKey = "database/2026/replication.pgdump";
            var targets = includeSecondOffsite
                ? new[] { new StorageReplicationTarget("offsite-a", "host-a"), new StorageReplicationTarget("offsite-b", "host-b") }
                : [new StorageReplicationTarget("offsite-a", "host-a")];
            await using (var registrationContext = new GarageBalanceDbContext(dbOptions))
            {
                var catalog = new EfStorageCatalog(registrationContext);
                await catalog.RegisterCommittedObjectAsync(
                    new RegisterCommittedStorageObjectRequest(
                        operationId,
                        "garagebalance",
                        StorageDataClass.DatabaseBackup,
                        logicalKey,
                        "database-backups",
                        1,
                        1,
                        bytes.Length,
                        sha256,
                        "replication.pgdump",
                        "application/octet-stream",
                        "local-hot",
                        "local-host",
                        "replication.pgdump",
                        targets,
                        4,
                        Now),
                    CancellationToken.None);
            }

            var local = new FakeStorageProvider("local-hot");
            local.Seed("replication.pgdump", bytes);
            var offsiteA = new FakeStorageProvider("offsite-a");
            var offsiteB = new FakeStorageProvider("offsite-b");
            var registry = new FakeProviderRegistry(local, offsiteA, offsiteB);
            var options = CreateStorageOptions(includeSecondOffsite);
            var resolver = new StorageConfigurationResolver(
                Options.Create(options),
                Options.Create(new DatabaseBackupOptions { Directory = "unused" }));
            var clock = new MutableTimeProvider(Now);
            var runnerContext = new GarageBalanceDbContext(dbOptions);
            var runner = new StorageReplicationRunner(
                new EfStorageCatalog(runnerContext),
                registry,
                resolver,
                clock,
                NullLogger<StorageReplicationRunner>.Instance);
            return new ReplicationFixture(connection, dbOptions, operationId, logicalKey, runnerContext, clock, local, offsiteA, offsiteB, runner);
        }

        public GarageBalanceDbContext CreateContext() => new(dbOptions);

        public string ExpectedRemoteLocator(string destinationId) =>
            FakeStorageProvider.BuildLocator(destinationId, operationId, 1, logicalKey);

        public async ValueTask DisposeAsync()
        {
            await runnerContext.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static StorageOptions CreateStorageOptions(bool includeSecondOffsite)
        {
            var destinations = new List<StorageDestinationOptions>
            {
                Destination("local-hot", "local-host"),
                Destination("offsite-a", "host-a")
            };
            if (includeSecondOffsite)
            {
                destinations.Add(Destination("offsite-b", "host-b"));
            }
            return new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations = destinations,
                Pools = [new StoragePoolOptions { Id = "database-backups", DestinationIds = destinations.Select(item => item.Id).ToList() }],
                Policies =
                [
                    new StoragePolicyOptions
                    {
                        Id = "database-backups",
                        DataClass = StorageDataClass.DatabaseBackup,
                        PoolId = "database-backups",
                        RequiredIndependentCopies = 2,
                        DesiredCopies = includeSecondOffsite ? 3 : 2,
                        MinimumOffsiteCopies = 1
                    }
                ],
                Replication = new StorageReplicationOptions { PollSeconds = 1, LeaseSeconds = 60, MaximumAttempts = 4 }
            };
        }

        private static StorageDestinationOptions Destination(string id, string failureDomain) => new()
        {
            Id = id,
            Type = StorageProviderType.LocalFileSystem,
            FailureDomain = failureDomain,
            RootPath = "unused",
            Capabilities = ["Read", "Write", "Stat", "Delete"]
        };
    }

    private sealed class FakeProviderRegistry(params FakeStorageProvider[] providers) : IStorageProviderRegistry
    {
        private readonly IReadOnlyDictionary<string, IStorageProvider> items = providers
            .ToDictionary(item => item.DestinationId, item => (IStorageProvider)item, StringComparer.Ordinal);
        public IStorageProvider GetRequired(string destinationId) => items[destinationId];
        public IReadOnlyList<IStorageProvider> GetAll() => items.Values.ToArray();
    }

    private sealed class FakeStorageProvider(string destinationId) : IStorageProvider
    {
        private readonly Dictionary<string, byte[]> objects = new(StringComparer.Ordinal);
        public string DestinationId { get; } = destinationId;
        public StorageCapability Capabilities => StorageCapability.Read | StorageCapability.Write | StorageCapability.Stat | StorageCapability.Delete;
        public StorageProviderException? WriteFailure { get; set; }
        public bool LoseResponseAfterWrite { get; set; }
        public int WriteCalls { get; private set; }

        public void Seed(string locator, byte[] bytes) => objects[locator] = bytes.ToArray();

        public string GetWriteLocator(StorageWriteRequest request) =>
            BuildLocator(DestinationId, request.OperationId, request.Generation, request.ObjectKey);

        public static string BuildLocator(string destinationId, Guid operationId, long generation, string objectKey) =>
            $"{destinationId}/{operationId:N}/g{generation:D20}/{StorageObjectKey.Normalize(objectKey)}";

        public async Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken cancellationToken)
        {
            WriteCalls++;
            if (WriteFailure is not null)
            {
                throw WriteFailure;
            }
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            if (bytes.LongLength != request.SizeBytes ||
                !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), request.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageProviderException(StorageErrorCategory.ChecksumOrStale, "bad source");
            }
            var locator = GetWriteLocator(request);
            objects[locator] = bytes;
            if (LoseResponseAfterWrite)
            {
                throw new StorageProviderException(StorageErrorCategory.UnknownOutcome, "response lost");
            }
            return new StorageWriteResult(locator, "v1", request.Sha256);
        }

        public Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!objects.TryGetValue(nativeLocator, out var bytes))
            {
                throw new StorageProviderException(StorageErrorCategory.ObjectMissing, "missing");
            }
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!objects.TryGetValue(nativeLocator, out var bytes))
            {
                return Task.FromResult<StorageObjectStat?>(null);
            }
            return Task.FromResult<StorageObjectStat?>(new StorageObjectStat(
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                "v1",
                new Dictionary<string, string>()));
        }

        public Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            objects.Remove(nativeLocator);
            return Task.CompletedTask;
        }

        public Task<StorageDownloadLink?> GetDownloadLinkAsync(string nativeLocator, TimeSpan lifetime, CancellationToken cancellationToken) =>
            Task.FromResult<StorageDownloadLink?>(null);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
