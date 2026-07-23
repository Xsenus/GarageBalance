using System.Security.Cryptography;
using System.Text;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlDemoDatasetIsolationIntegrationTests
{
    private const string MigrationId = "20260723002908_RemoveDemoDatasetFromWorkingDatabases";

    [PostgreSqlFact]
    public async Task CleanupMigration_RemovesSeedRowsAndPreservesCustomerAndManualRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var removableOwnerId = DemoId("garagebalance-demo-owner-1");
        var removableGarageId = DemoId("garagebalance-demo-garage-101");
        var retainedOwnerId = DemoId("garagebalance-demo-owner-2");
        var retainedGarageId = DemoId("garagebalance-demo-garage-102");
        var customerOwnerId = Guid.NewGuid();
        var customerGarageId = Guid.NewGuid();
        var manualReadingId = Guid.NewGuid();
        var customerReadingId = Guid.NewGuid();

        await using (var setupContext = database.CreateContext())
        {
            setupContext.Owners.AddRange(
                new Owner { Id = removableOwnerId, LastName = "Демо", FirstName = "Удалить" },
                new Owner { Id = retainedOwnerId, LastName = "Демо", FirstName = "Сохранить" },
                new Owner { Id = customerOwnerId, LastName = "Заказчик", FirstName = "Данные" });
            setupContext.Garages.AddRange(
                new Garage { Id = removableGarageId, Number = "101", OwnerId = removableOwnerId },
                new Garage { Id = retainedGarageId, Number = "102", OwnerId = retainedOwnerId },
                new Garage { Id = customerGarageId, Number = "CUSTOMER-1", OwnerId = customerOwnerId });
            setupContext.MeterReadings.AddRange(
                DemoReading(101, removableGarageId),
                DemoReading(102, retainedGarageId),
                new MeterReading
                {
                    Id = manualReadingId,
                    GarageId = retainedGarageId,
                    MeterKind = "water",
                    AccountingMonth = new DateOnly(2021, 2, 1),
                    ReadingDate = new DateOnly(2021, 2, 20),
                    CurrentValue = 20m
                },
                new MeterReading
                {
                    Id = customerReadingId,
                    GarageId = customerGarageId,
                    MeterKind = "water",
                    AccountingMonth = new DateOnly(2021, 1, 1),
                    ReadingDate = new DateOnly(2021, 1, 20),
                    CurrentValue = 30m
                });
            await setupContext.SaveChangesAsync();
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = {MigrationId};
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        Assert.Null(await verificationContext.Garages.FindAsync(removableGarageId));
        Assert.Null(await verificationContext.Owners.FindAsync(removableOwnerId));
        Assert.NotNull(await verificationContext.Garages.FindAsync(retainedGarageId));
        Assert.NotNull(await verificationContext.Owners.FindAsync(retainedOwnerId));
        Assert.NotNull(await verificationContext.MeterReadings.FindAsync(manualReadingId));
        Assert.NotNull(await verificationContext.Garages.FindAsync(customerGarageId));
        Assert.NotNull(await verificationContext.Owners.FindAsync(customerOwnerId));
        Assert.NotNull(await verificationContext.MeterReadings.FindAsync(customerReadingId));
        Assert.DoesNotContain(await verificationContext.MeterReadings.ToListAsync(), reading =>
            reading.Id == DemoId("garagebalance-demo-meter-101-water-202101") ||
            reading.Id == DemoId("garagebalance-demo-meter-102-water-202101"));
    }

    [PostgreSqlFact]
    public async Task CleanupMigration_PreservesDemoRowsWhenDatabaseExplicitlyOptsIn()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var ownerId = DemoId("garagebalance-demo-owner-1");
        var garageId = DemoId("garagebalance-demo-garage-101");
        var reading = DemoReading(101, garageId);

        await using (var setupContext = database.CreateContext())
        {
            setupContext.Owners.Add(new Owner { Id = ownerId, LastName = "Демо", FirstName = "Стенд" });
            setupContext.Garages.Add(new Garage { Id = garageId, Number = "101", OwnerId = ownerId });
            setupContext.MeterReadings.Add(reading);
            await setupContext.SaveChangesAsync();
            await setupContext.Database.OpenConnectionAsync();
            await setupContext.Database.ExecuteSqlRawAsync(
                "SET garagebalance.demo_seed_enabled = 'on'");
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = {MigrationId};
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        Assert.NotNull(await verificationContext.Owners.FindAsync(ownerId));
        Assert.NotNull(await verificationContext.Garages.FindAsync(garageId));
        Assert.NotNull(await verificationContext.MeterReadings.FindAsync(reading.Id));
    }

    private static MeterReading DemoReading(int garageNumber, Guid garageId) => new()
    {
        Id = DemoId($"garagebalance-demo-meter-{garageNumber}-water-202101"),
        GarageId = garageId,
        MeterKind = "water",
        AccountingMonth = new DateOnly(2021, 1, 1),
        ReadingDate = new DateOnly(2021, 1, 20),
        CurrentValue = 10m,
        Comment = "Демонстрационное ежемесячное показание."
    };

    private static Guid DemoId(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return Guid.ParseExact(Convert.ToHexString(hash), "N");
    }
}
