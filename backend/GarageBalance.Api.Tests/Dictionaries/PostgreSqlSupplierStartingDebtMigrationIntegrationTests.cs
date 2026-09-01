using GarageBalance.Api.Infrastructure.Data.Migrations;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlSupplierStartingDebtMigrationIntegrationTests
{
    private const string PreviousMigration = "20260831023447_ReconcileFeeCampaignPrincipals";
    private const string SupplierStartingDebtMigration = "20260831210726_AddSupplierStartingDebt";

    [Fact]
    public void RawSqlOperations_AreTerminatedForIdempotentMigrationScripts()
    {
        var migrations = new Migration[]
        {
            new AddSupplierStartingDebt(),
            new EnforceSupplierOpeningBalanceConsistency()
        };
        var sqlOperations = migrations.SelectMany(migration => migration.UpOperations.OfType<SqlOperation>()).ToArray();

        Assert.NotEmpty(sqlOperations);
        Assert.All(sqlOperations, operation => Assert.EndsWith(";", operation.Sql.TrimEnd(), StringComparison.Ordinal));
    }

    [PostgreSqlFact]
    public async Task MigrationBackfillsLegacySupplierDebtAndProtectsItsRelationToBalance()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var groupId = Guid.NewGuid();
        var debtSupplierId = Guid.NewGuid();
        var advanceSupplierId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.UtcNow;

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "supplier_groups"
                    ("Id", "Name", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({groupId}, {"Миграция задолженности поставщиков"}, FALSE, FALSE, {createdAtUtc}, {createdAtUtc});

                INSERT INTO "suppliers"
                    ("Id", "Name", "GroupId", "StartingBalance", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({debtSupplierId}, {"Поставщик с долгом"}, {groupId}, {125.50m}, FALSE, {createdAtUtc}, {createdAtUtc}),
                    ({advanceSupplierId}, {"Поставщик с авансом"}, {groupId}, {-50m}, FALSE, {createdAtUtc}, {createdAtUtc});
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var debtSupplier = await verificationContext.Suppliers.FindAsync(debtSupplierId);
            var advanceSupplier = await verificationContext.Suppliers.FindAsync(advanceSupplierId);

            Assert.Equal(125.50m, debtSupplier!.StartingDebt);
            Assert.Equal(0m, advanceSupplier!.StartingDebt);
        }

        await using var invalidContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            invalidContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "suppliers"
                SET "StartingDebt" = {126m}
                WHERE "Id" = {debtSupplierId};
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [PostgreSqlFact]
    public async Task ConsistencyMigration_AlignsExistingDebtWithBalanceAndRejectsEveryConflict()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var groupId = Guid.NewGuid();
        var debtSupplierId = Guid.NewGuid();
        var advanceSupplierId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.UtcNow;

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(SupplierStartingDebtMigration);
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "supplier_groups"
                    ("Id", "Name", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({groupId}, {"Согласование начального долга"}, FALSE, FALSE, {createdAtUtc}, {createdAtUtc});

                INSERT INTO "suppliers"
                    ("Id", "Name", "GroupId", "StartingBalance", "StartingDebt", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({debtSupplierId}, {"Несогласованный долг"}, {groupId}, {125m}, {13m}, FALSE, {createdAtUtc}, {createdAtUtc}),
                    ({advanceSupplierId}, {"Аванс"}, {groupId}, {-50m}, {0m}, FALSE, {createdAtUtc}, {createdAtUtc});
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var debtSupplier = await verificationContext.Suppliers.FindAsync(debtSupplierId);
            var advanceSupplier = await verificationContext.Suppliers.FindAsync(advanceSupplierId);

            Assert.Equal(125m, debtSupplier!.StartingBalance);
            Assert.Equal(125m, debtSupplier.StartingDebt);
            Assert.Equal(-50m, advanceSupplier!.StartingBalance);
            Assert.Equal(0m, advanceSupplier.StartingDebt);
        }

        await using (var mismatchedDebtContext = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                mismatchedDebtContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "suppliers"
                    SET "StartingDebt" = {13m}
                    WHERE "Id" = {debtSupplierId};
                    """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        await using (var advanceWithDebtContext = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                advanceWithDebtContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "suppliers"
                    SET "StartingBalance" = {-50m}, "StartingDebt" = {10m}
                    WHERE "Id" = {debtSupplierId};
                    """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        }

        await using var validContext = database.CreateContext();
        var affected = await validContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "suppliers"
            SET "StartingBalance" = {-25m}, "StartingDebt" = {0m}
            WHERE "Id" = {debtSupplierId};
            """);
        Assert.Equal(1, affected);
    }
}
