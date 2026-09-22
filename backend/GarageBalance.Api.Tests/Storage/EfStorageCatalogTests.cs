using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarageBalance.Api.Tests.Storage;

public sealed class EfStorageCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DomainStateMachines_ProtectCommittedGenerationAndRequiredCopySemantics()
    {
        var storageObject = new StorageObject();

        storageObject.CommitLocal(1, 4, new string('a', 64), Now);
        storageObject.SetProtection(1, requiredCopies: 2, desiredCopies: 3, Now);
        Assert.Equal(StorageObjectState.ProtectionDegraded, storageObject.State);
        storageObject.SetProtection(2, requiredCopies: 2, desiredCopies: 3, Now);
        Assert.Equal(StorageObjectState.Protected, storageObject.State);
        storageObject.BeginDelete(Now);
        Assert.Throws<InvalidOperationException>(() => storageObject.SetProtection(3, 2, 3, Now));

        var replica = new StorageObjectReplica { State = StorageReplicaState.Uploading };
        replica.MarkUnknown("Provider response was lost.", Now);
        Assert.Equal(StorageReplicaState.Unknown, replica.State);
        replica.MarkAvailable(4, new string('b', 64), "etag", Now);
        Assert.Equal(StorageReplicaState.Available, replica.State);
        replica.BeginDelete(Now);
        Assert.Throws<InvalidOperationException>(() => replica.MarkAvailable(4, new string('b', 64), "etag", Now));
    }

    [Fact]
    public async Task Registration_IsAtomicIdempotentAndExportsManifestWithoutSecrets()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-storage-catalog-").FullName;
        var databasePath = Path.Combine(root, "catalog.db");
        await using var provider = BuildProvider(databasePath);
        try
        {
            await EnsureDatabaseAsync(provider);
            var request = CreateRegistration();
            StorageCatalogRegistration first;
            using (var scope = provider.CreateScope())
            {
                first = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .RegisterCommittedObjectAsync(request, CancellationToken.None);
            }
            using (var scope = provider.CreateScope())
            {
                var second = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .RegisterCommittedObjectAsync(request, CancellationToken.None);
                Assert.True(second.AlreadyExisted);
                Assert.Equal(first.Object.Id, second.Object.Id);
                Assert.Equal(2, second.Jobs.Count);
            }
            using (var scope = provider.CreateScope())
            {
                var manifest = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ExportManifestAsync(StorageDataClass.DatabaseBackup, 10, CancellationToken.None);
                var entry = Assert.Single(manifest);
                Assert.Equal(request.Sha256, entry.Sha256);
                Assert.Equal("database/2026/copy.pgdump", entry.LogicalKey);
                Assert.Equal(3, entry.Replicas.Count);
                Assert.DoesNotContain("password", entry.Replicas[0].NativeLocator, StringComparison.OrdinalIgnoreCase);
            }

            using var verificationScope = provider.CreateScope();
            var db = verificationScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
            Assert.Equal(1, await db.StorageObjects.CountAsync());
            Assert.Equal(3, await db.StorageObjectReplicas.CountAsync());
            Assert.Equal(2, await db.StorageTransferJobs.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Registration_RejectsOperationIdReuseAndDuplicateLogicalObjectWithoutPartialRows()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-storage-conflict-").FullName;
        var databasePath = Path.Combine(root, "catalog.db");
        await using var provider = BuildProvider(databasePath);
        try
        {
            await EnsureDatabaseAsync(provider);
            var request = CreateRegistration();
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .RegisterCommittedObjectAsync(request, CancellationToken.None);
            }
            using (var scope = provider.CreateScope())
            {
                var catalog = scope.ServiceProvider.GetRequiredService<IStorageCatalog>();
                var changedHash = request with { Sha256 = new string('b', 64) };
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => catalog.RegisterCommittedObjectAsync(changedHash, CancellationToken.None));
            }
            using (var scope = provider.CreateScope())
            {
                var duplicateLogical = request with { OperationId = Guid.NewGuid() };
                await Assert.ThrowsAnyAsync<DbUpdateException>(
                    () => scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                        .RegisterCommittedObjectAsync(duplicateLogical, CancellationToken.None));
            }

            using var verificationScope = provider.CreateScope();
            var db = verificationScope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
            Assert.Equal(1, await db.StorageObjects.CountAsync());
            Assert.Equal(3, await db.StorageObjectReplicas.CountAsync());
            Assert.Equal(2, await db.StorageTransferJobs.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JobLease_IsExclusiveAndExpiredLeaseCanResumeAfterRestart()
    {
        var root = Directory.CreateTempSubdirectory("garagebalance-storage-lease-").FullName;
        var databasePath = Path.Combine(root, "catalog.db");
        await using var provider = BuildProvider(databasePath);
        try
        {
            await EnsureDatabaseAsync(provider);
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .RegisterCommittedObjectAsync(CreateRegistration(), CancellationToken.None);
            }

            StorageTransferJob firstLease;
            using (var scope = provider.CreateScope())
            {
                firstLease = (await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ClaimNextJobAsync("worker-a", TimeSpan.FromMinutes(2), Now, CancellationToken.None))!;
            }
            Assert.NotNull(firstLease);
            Assert.Equal(1, firstLease.AttemptCount);
            using (var scope = provider.CreateScope())
            {
                var otherJob = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ClaimNextJobAsync("worker-b", TimeSpan.FromMinutes(2), Now, CancellationToken.None);
                Assert.NotNull(otherJob);
                Assert.NotEqual(firstLease.Id, otherJob.Id);
            }
            using (var scope = provider.CreateScope())
            {
                var none = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ClaimNextJobAsync("worker-c", TimeSpan.FromMinutes(2), Now, CancellationToken.None);
                Assert.Null(none);
            }
            using (var scope = provider.CreateScope())
            {
                var resumed = await scope.ServiceProvider.GetRequiredService<IStorageCatalog>()
                    .ClaimNextJobAsync("worker-after-restart", TimeSpan.FromMinutes(2), Now.AddMinutes(3), CancellationToken.None);
                Assert.NotNull(resumed);
                Assert.Equal(2, resumed.AttemptCount);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<GarageBalanceDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IStorageCatalog, EfStorageCatalog>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>().Database.EnsureCreatedAsync();
    }

    private static RegisterCommittedStorageObjectRequest CreateRegistration() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        "garagebalance",
        StorageDataClass.DatabaseBackup,
        "database\\2026\\copy.pgdump",
        "database-backups",
        1,
        1,
        4,
        new string('a', 64),
        "copy.pgdump",
        "application/octet-stream",
        "local-hot",
        "local-host",
        "copy.pgdump",
        [new StorageReplicationTarget("offsite-a", "host-a"), new StorageReplicationTarget("offsite-b", "host-b")],
        12,
        Now);
}
