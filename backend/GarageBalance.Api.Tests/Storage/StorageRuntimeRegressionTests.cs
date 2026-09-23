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

public sealed class StorageRuntimeRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task KeysetPagesVisitEveryObjectDespiteTimestampChangesAndExcludeOtherTenants()
    {
        await using var fixture = await Fixture.CreateAsync(205);
        var foreign = Object(999);
        foreign.TenantId = "another-tenant";
        fixture.Db.StorageObjects.Add(foreign);
        await fixture.Db.SaveChangesAsync();
        var keys = new List<string>();
        string? cursor = null;
        do
        {
            var page = await fixture.Catalog.ExportManifestPageAsync("garagebalance", StorageDataClass.DatabaseBackup, 100, cursor, CancellationToken.None);
            keys.AddRange(page.Items.Select(item => item.LogicalKey));
            foreach (var item in page.Items)
            {
                Assert.True(await fixture.Catalog.RecordReplicaVerifiedAsync(item.ObjectId, "local-hot", 1, Hash, 4, Now, CancellationToken.None));
            }
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(205, keys.Count);
        Assert.Equal(205, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(foreign.LogicalKey, keys);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.ExportManifestPageAsync("garagebalance", StorageDataClass.DatabaseBackup, 0, null, CancellationToken.None));
    }

    [Fact]
    public async Task ProtectionMonitorCountsEveryPageAndBothDataClassesOnlyForItsTenant()
    {
        await using var fixture = await Fixture.CreateAsync(505);
        var recovery = Object(999);
        recovery.DataClass = StorageDataClass.RecoverySecrets;
        recovery.State = StorageObjectState.ProtectionDegraded;
        var foreign = Object(1000);
        foreign.TenantId = "foreign";
        foreign.State = StorageObjectState.Failed;
        fixture.Db.StorageObjects.AddRange(recovery, foreign);
        await fixture.Db.SaveChangesAsync();
        var summary = await StorageProtectionSummary.ReadAsync(fixture.Catalog, "garagebalance",
            [StorageDataClass.DatabaseBackup, StorageDataClass.RecoverySecrets], CancellationToken.None);
        Assert.Equal(505, summary.Protected);
        Assert.Equal(1, summary.Degraded);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Pending);
        Assert.Equal(0, summary.Deleting);
    }

    [Fact]
    public async Task ReconciliationScansAllPagesAndTargetedVerifyFindsOldObjects()
    {
        await using var fixture = await Fixture.CreateAsync(205);
        Assert.Equal(0, await fixture.Reconciliation.ReconcileOnceAsync(CancellationToken.None));
        Assert.Equal(205, fixture.Provider.StatCalls);
        Assert.Equal(205, await fixture.Db.StorageObjectReplicas.CountAsync(item => item.LastVerifiedAtUtc == Now));

        fixture.Clock.Now = Now.AddHours(1);
        Assert.Equal(0, await fixture.Reconciliation.ReconcileObjectAsync("backups/0000.pgdump", CancellationToken.None));
        Assert.Equal(206, fixture.Provider.StatCalls);
        var oldest = await fixture.Db.StorageObjectReplicas.SingleAsync(item => item.NativeLocator == "backups/0000.pgdump");
        Assert.Equal(Now.AddHours(1), oldest.LastVerifiedAtUtc);
        await fixture.Reconciliation.ReconcileObjectAsync("backups/absent.pgdump", CancellationToken.None);
        Assert.Equal(206, fixture.Provider.StatCalls);
    }

    [Fact]
    public async Task MassAnomaliesPauseBeforeSchedulingAnyRepairAndRepeatedCyclesStayPaused()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-reconciliation-mass-").FullName;
        try
        {
            await using var fixture = await Fixture.CreateAsync(11);
            fixture.Provider.Missing = true;
            var guard = FileStorageReconciliationGuardTests.Create(root);
            var runner = fixture.CreateReconciliation(guard);
            Assert.Equal(0, await runner.ReconcileOnceAsync(CancellationToken.None));
            Assert.Equal(11, fixture.Provider.StatCalls);
            Assert.True((await guard.GetStateAsync(CancellationToken.None)).Paused);
            Assert.Equal(0, await fixture.Db.StorageTransferJobs.CountAsync());
            var restarted = fixture.CreateReconciliation(FileStorageReconciliationGuardTests.Create(root));
            Assert.Equal(0, await restarted.ReconcileOnceAsync(CancellationToken.None));
            Assert.Equal(11, fixture.Provider.StatCalls);
            await Assert.ThrowsAsync<StorageProviderException>(() => restarted.ReconcileObjectAsync("backups/0000.pgdump", CancellationToken.None));
            fixture.Provider.Missing = false;
            await guard.ResumeAsync("Provider verified by operator", CancellationToken.None);
            Assert.Equal(0, await restarted.ReconcileOnceAsync(CancellationToken.None));
            Assert.Equal(22, fixture.Provider.StatCalls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task VerificationDoesNotResurrectTombstonesOrAcceptWrongGenerationOrHash()
    {
        await using var fixture = await Fixture.CreateAsync(1);
        var item = await fixture.Db.StorageObjects.SingleAsync();
        Assert.False(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 2, Hash, 4, Now, CancellationToken.None));
        Assert.False(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 1, new string('b', 64), 4, Now, CancellationToken.None));
        Assert.False(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 1, Hash, 5, Now, CancellationToken.None));
        item.BeginDelete(Now);
        await fixture.Db.SaveChangesAsync();
        Assert.False(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 1, Hash, 4, Now.AddHours(1), CancellationToken.None));
        Assert.Equal(StorageObjectState.Deleting, item.State);
    }

    [Fact]
    public async Task ActualOffsiteRequirementKeepsObjectDegradedUntilRemoteReplicaIsVerified()
    {
        await using var fixture = await Fixture.CreateAsync(1, minimumOffsiteCopies: 1);
        var item = await fixture.Db.StorageObjects.Include(value => value.Replicas).SingleAsync();
        fixture.Db.StorageObjectReplicas.Add(Replica(item, "local-second", "second-host"));
        fixture.Db.StorageObjectReplicas.Add(Replica(item, "offsite-a", "remote-host", StorageReplicaState.Missing));
        await fixture.Db.SaveChangesAsync();
        Assert.True(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 1, Hash, 4, Now, CancellationToken.None));
        Assert.Equal(StorageObjectState.ProtectionDegraded, item.State);
        Assert.True(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "offsite-a", 1, Hash, 4, Now, CancellationToken.None));
        Assert.Equal(StorageObjectState.Protected, item.State);
    }

    [Theory]
    [InlineData("pool")]
    [InlineData("failure-domain")]
    [InlineData("capability")]
    public async Task CurrentPolicyExcludesIneligibleReplicaFromPersistedProtection(string restriction)
    {
        await using var fixture = await Fixture.CreateAsync(1, configure: options =>
        {
            if (restriction == "pool")
            {
                options.Pools[0].DestinationIds.Remove("local-hot");
            }
            if (restriction == "capability")
            {
                options.Destinations[0].Capabilities.Remove("Read");
            }
        });
        var item = await fixture.Db.StorageObjects.Include(value => value.Replicas).SingleAsync();
        if (restriction == "failure-domain")
        {
            item.Replicas[0].FailureDomain = "retired-host";
            await fixture.Db.SaveChangesAsync();
        }
        Assert.True(await fixture.Catalog.RecordReplicaVerifiedAsync(item.Id, "local-hot", 1, Hash, 4, Now, CancellationToken.None));
        Assert.NotEqual(StorageObjectState.Protected, item.State);
    }

    [Fact]
    public async Task FilteredClaimNeverTakesDeleteOrAnObjectOutsideSnapshot()
    {
        await using var fixture = await Fixture.CreateAsync(2);
        var items = await fixture.Db.StorageObjects.OrderBy(item => item.LogicalKey).ToArrayAsync();
        fixture.Db.StorageTransferJobs.AddRange(
            Job(items[0], StorageTransferJobKind.Delete),
            Job(items[1], StorageTransferJobKind.Replicate),
            Job(items[0], StorageTransferJobKind.Repair));
        await fixture.Db.SaveChangesAsync();
        var claimed = await fixture.Catalog.ClaimNextJobAsync("migration", TimeSpan.FromMinutes(1), Now,
            [StorageTransferJobKind.Replicate, StorageTransferJobKind.Repair], CancellationToken.None, [items[0].Id]);
        Assert.NotNull(claimed);
        Assert.Equal(StorageTransferJobKind.Repair, claimed.Kind);
        Assert.Equal(items[0].Id, claimed.StorageObjectId);
        Assert.Null(await fixture.Catalog.ClaimNextJobAsync("migration", TimeSpan.FromMinutes(1), Now,
            [StorageTransferJobKind.Replicate, StorageTransferJobKind.Repair], CancellationToken.None, [items[0].Id]));
        var untouched = await fixture.Db.StorageTransferJobs.Where(item => item.Id != claimed.Id).ToArrayAsync();
        Assert.All(untouched, item => Assert.Equal(StorageTransferJobState.Ready, item.State));
    }

    [Fact]
    public async Task AddingDestinationToExistingCatalogCreatesDebtOnceAndNeverResurrectsDeletedObject()
    {
        await using var fixture = await Fixture.CreateAsync(1);
        var item = await fixture.Db.StorageObjects.SingleAsync();
        Assert.True(await fixture.Catalog.EnsureTargetsAsync(item.Id, "database-backups", 2,
            [new StorageReplicationTarget("offsite-a", "remote-host")], 4, Now, CancellationToken.None));
        Assert.False(await fixture.Catalog.EnsureTargetsAsync(item.Id, "database-backups", 2,
            [new StorageReplicationTarget("offsite-a", "remote-host")], 4, Now, CancellationToken.None));
        Assert.Equal(2, await fixture.Db.StorageObjectReplicas.CountAsync());
        Assert.Equal(2, (await fixture.Db.StorageTransferJobs.SingleAsync()).PolicyRevision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.EnsureTargetsAsync(item.Id, "database-backups", 1,
            [new StorageReplicationTarget("offsite-a", "remote-host")], 4, Now, CancellationToken.None));
        item.BeginDelete(Now);
        await fixture.Db.SaveChangesAsync();
        Assert.False(await fixture.Catalog.EnsureTargetsAsync(item.Id, "database-backups", 2,
            [new StorageReplicationTarget("local-second", "second-host")], 4, Now, CancellationToken.None));
        Assert.Equal(2, await fixture.Db.StorageObjectReplicas.CountAsync());
    }

    [Fact]
    public async Task DeleteWaitsForInFlightWriteAndSchedulesCorruptedReplicaForPhysicalDeletion()
    {
        await using var fixture = await Fixture.CreateAsync(1);
        var item = await fixture.Db.StorageObjects.Include(value => value.Replicas).SingleAsync();
        var corrupted = Replica(item, "offsite-a", "remote-host", StorageReplicaState.Corrupted);
        fixture.Db.StorageObjectReplicas.Add(corrupted);
        var upload = Job(item, StorageTransferJobKind.Replicate);
        upload.TryLease("upload", Now, TimeSpan.FromMinutes(6));
        fixture.Db.StorageTransferJobs.Add(upload);
        await fixture.Db.SaveChangesAsync();
        await fixture.Catalog.TombstoneAndScheduleDeleteAsync("garagebalance", StorageDataClass.DatabaseBackup,
            item.LogicalKey, 4, Now, CancellationToken.None);
        var deletions = await fixture.Db.StorageTransferJobs.Where(job => job.Kind == StorageTransferJobKind.Delete).ToArrayAsync();
        Assert.Equal(2, deletions.Length);
        Assert.All(deletions, job => Assert.Equal(Now.AddMinutes(6), job.DueAtUtc));
        Assert.Equal(StorageReplicaState.Deleting, corrupted.State);
        Assert.Null(await fixture.Catalog.ClaimNextJobAsync("deleter", TimeSpan.FromMinutes(1), Now,
            [StorageTransferJobKind.Delete], CancellationToken.None));
    }

    [Fact]
    public async Task MaintenanceLockExcludesOtherInstancesAndReleasesOnDispose()
    {
        await using var fixture = await Fixture.CreateAsync(0);
        var first = new StorageMaintenanceLock(fixture.Db);
        var second = new StorageMaintenanceLock(fixture.Db);
        var scope = "backups-test-" + Guid.NewGuid().ToString("N");
        await using (var lease = await first.TryAcquireAsync(scope, CancellationToken.None))
        {
            Assert.NotNull(lease);
            Assert.Null(await second.TryAcquireAsync(scope, CancellationToken.None));
            await using var unrelated = await second.TryAcquireAsync(scope + "other", CancellationToken.None);
            Assert.NotNull(unrelated);
        }
        await using var next = await second.TryAcquireAsync(scope, CancellationToken.None);
        Assert.NotNull(next);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.TryAcquireAsync(scope, new CancellationToken(true)));
    }

    private static StorageTransferJob Job(StorageObject item, StorageTransferJobKind kind) => new()
    {
        StorageObjectId = item.Id,
        StorageObjectReplicaId = item.Replicas[0].Id,
        DestinationId = "local-hot",
        Generation = 1,
        Kind = kind,
        DueAtUtc = Now,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now,
        IdempotencyKey = Guid.NewGuid().ToString("N")
    };

    private static StorageObject Object(int index)
    {
        var item = new StorageObject
        {
            OperationId = Guid.NewGuid(),
            LogicalKey = $"backups/{index:D4}.pgdump",
            OriginalFileName = $"{index:D4}.pgdump",
            DataClass = StorageDataClass.DatabaseBackup,
            PolicyId = "database-backups",
            CommittedGeneration = 1,
            SizeBytes = 4,
            Sha256 = Hash,
            State = StorageObjectState.Protected,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now.AddDays(-1)
        };
        item.Replicas.Add(Replica(item, "local-hot", "local-host"));
        return item;
    }

    private static StorageObjectReplica Replica(StorageObject item, string destination, string failureDomain,
        StorageReplicaState state = StorageReplicaState.Available) => new()
        {
            StorageObject = item,
            DestinationId = destination,
            FailureDomain = failureDomain,
            NativeLocator = item.LogicalKey,
            Generation = 1,
            State = state,
            SizeBytes = 4,
            Sha256 = Hash,
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now.AddDays(-1),
            LastVerifiedAtUtc = Now.AddDays(-1)
        };

    private sealed class Fixture(SqliteConnection connection, GarageBalanceDbContext db, EfStorageCatalog catalog,
        MemoryProvider provider, Clock clock, StorageReconciliationRunner reconciliation,
        StorageConfigurationResolver resolver) : IAsyncDisposable
    {
        public GarageBalanceDbContext Db { get; } = db;
        public EfStorageCatalog Catalog { get; } = catalog;
        public MemoryProvider Provider { get; } = provider;
        public Clock Clock { get; } = clock;
        public StorageReconciliationRunner Reconciliation { get; } = reconciliation;
        public StorageReconciliationRunner CreateReconciliation(IStorageReconciliationGuard guard) =>
            new(Catalog, new Registry(Provider), resolver, new StorageOperationHealthTracker(Clock), Clock,
                NullLogger<StorageReconciliationRunner>.Instance, guard);

        public static async Task<Fixture> CreateAsync(int count, int minimumOffsiteCopies = 0, Action<StorageOptions>? configure = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.StorageObjects.AddRange(Enumerable.Range(0, count).Select(Object));
            await db.SaveChangesAsync();
            var options = new StorageOptions
            {
                Mode = StorageMode.AsyncMirror,
                Destinations =
                [
                    new() { Id = "local-hot", Type = StorageProviderType.LocalFileSystem, RootPath = "unused", FailureDomain = "local-host", Capabilities = ["Read", "Write", "Stat"] },
                    new() { Id = "local-second", Type = StorageProviderType.LocalFileSystem, RootPath = "unused", FailureDomain = "second-host", Capabilities = ["Read", "Write", "Stat"] },
                    new() { Id = "offsite-a", Type = StorageProviderType.S3Compatible, Bucket = "unused", FailureDomain = "remote-host", Capabilities = ["Read", "Write", "Stat"] }
                ],
                Pools = [new() { Id = "database-backups", DestinationIds = ["local-hot", "local-second", "offsite-a"] }],
                Policies = [new() { Id = "database-backups", PoolId = "database-backups", DataClass = StorageDataClass.DatabaseBackup,
                    RequiredIndependentCopies = minimumOffsiteCopies > 0 ? 2 : 1, DesiredCopies = 3, MinimumOffsiteCopies = minimumOffsiteCopies }]
            };
            configure?.Invoke(options);
            var resolver = new StorageConfigurationResolver(Options.Create(options), Options.Create(new DatabaseBackupOptions()));
            var catalog = new EfStorageCatalog(db, resolver);
            var provider = new MemoryProvider();
            var clock = new Clock { Now = StorageRuntimeRegressionTests.Now };
            var reconciliation = new StorageReconciliationRunner(catalog, new Registry(provider), resolver,
                new StorageOperationHealthTracker(clock), clock, NullLogger<StorageReconciliationRunner>.Instance);
            return new Fixture(connection, db, catalog, provider, clock, reconciliation, resolver);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Registry(MemoryProvider provider) : IStorageProviderRegistry
    {
        public IStorageProvider GetRequired(string destinationId) => provider;
        public IReadOnlyList<IStorageProvider> GetAll() => [provider];
    }

    private sealed class MemoryProvider : IStorageProvider
    {
        public string DestinationId => "local-hot";
        public StorageCapability Capabilities => StorageCapability.Read | StorageCapability.Stat;
        public int StatCalls { get; private set; }
        public bool Missing { get; set; }
        public Task<StorageObjectStat?> StatAsync(string nativeLocator, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatCalls++;
            return Task.FromResult<StorageObjectStat?>(Missing ? null : new StorageObjectStat(4, Hash, null, new Dictionary<string, string>()));
        }
        public string GetWriteLocator(StorageWriteRequest request) => request.ObjectKey;
        public Task<StorageWriteResult> WriteAsync(StorageWriteRequest request, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string nativeLocator, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string nativeLocator, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageDownloadLink?> GetDownloadLinkAsync(string nativeLocator, TimeSpan lifetime, CancellationToken cancellationToken) => Task.FromResult<StorageDownloadLink?>(null);
    }
}
