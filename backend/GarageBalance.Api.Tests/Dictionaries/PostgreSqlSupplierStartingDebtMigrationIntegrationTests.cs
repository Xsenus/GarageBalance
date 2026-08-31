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

    [Fact]
    public void RawSqlOperations_AreTerminatedForIdempotentMigrationScripts()
    {
        var migration = new AddSupplierStartingDebt();
        var sqlOperations = migration.UpOperations.OfType<SqlOperation>().ToArray();

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
}
