using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Domain.Storage;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Storage;

public sealed class PostgreSqlStorageCatalogIntegrationTests
{
    private const string PreviousMigration = "20260921033539_AddOwnerAdditionalPhones";

    [PostgreSqlFact]
    public async Task AdditiveMigration_UpgradesExistingSchemaAndCatalogSupportsExclusiveLease()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(PreviousMigration);
        await using (var migrationContext = database.CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
            Assert.Empty(await migrationContext.Database.GetPendingMigrationsAsync());
        }

        var now = new DateTimeOffset(2026, 9, 22, 5, 0, 0, TimeSpan.Zero);
        Guid firstJobId;
        await using (var context = database.CreateContext())
        {
            var catalog = new EfStorageCatalog(context);
            var registration = await catalog.RegisterCommittedObjectAsync(
                new RegisterCommittedStorageObjectRequest(
                    Guid.NewGuid(),
                    "garagebalance",
                    StorageDataClass.DatabaseBackup,
                    "database/2026/postgresql-copy.pgdump",
                    "database-backups",
                    1,
                    1,
                    4,
                    new string('a', 64),
                    "postgresql-copy.pgdump",
                    "application/octet-stream",
                    "local-hot",
                    "local-host",
                    "postgresql-copy.pgdump",
                    [new StorageReplicationTarget("offsite-a", "host-a")],
                    12,
                    now),
                CancellationToken.None);
            firstJobId = Assert.Single(registration.Jobs).Id;
        }

        await using var workerAContext = database.CreateContext();
        await using var workerBContext = database.CreateContext();
        var workerA = new EfStorageCatalog(workerAContext);
        var workerB = new EfStorageCatalog(workerBContext);
        var claims = await Task.WhenAll(
            workerA.ClaimNextJobAsync("worker-a", TimeSpan.FromMinutes(2), now, CancellationToken.None),
            workerB.ClaimNextJobAsync("worker-b", TimeSpan.FromMinutes(2), now, CancellationToken.None));

        Assert.Single(claims, claim => claim?.Id == firstJobId);
        Assert.Single(claims, claim => claim is not null);
        var winningCatalog = claims[0] is not null ? workerA : workerB;
        var winningOwner = claims[0] is not null ? "worker-a" : "worker-b";
        var transferContext = await winningCatalog.GetLeasedJobContextAsync(firstJobId, winningOwner, CancellationToken.None);
        Assert.Single(transferContext.AvailableSources);
        await winningCatalog.MarkReplicationUploadingAsync(
            firstJobId,
            winningOwner,
            "private/object/g00000000000000000001/postgresql-copy.pgdump",
            now,
            CancellationToken.None);
        await winningCatalog.CompleteReplicationAsync(
            firstJobId,
            winningOwner,
            "private/object/g00000000000000000001/postgresql-copy.pgdump",
            "version-1",
            new string('a', 64),
            requiredCopies: 2,
            desiredCopies: 2,
            now,
            CancellationToken.None);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(1, await verificationContext.StorageObjects.CountAsync());
        Assert.Equal(2, await verificationContext.StorageObjectReplicas.CountAsync());
        Assert.Equal(1, await verificationContext.StorageTransferJobs.CountAsync());
        Assert.Equal(StorageTransferJobState.Completed, (await verificationContext.StorageTransferJobs.SingleAsync()).State);
        Assert.Equal(StorageObjectState.Protected, (await verificationContext.StorageObjects.SingleAsync()).State);
    }
}
