using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlGarageStartingOverdueDebtMigrationIntegrationTests
{
    private const string PreviousMigration = "20260824031258_AddEpisodicExpenseRecipientAndFundConfirmation";

    [PostgreSqlFact]
    public async Task MigrationBackfillsLegacyBalancesAndProtectsOverduePart()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var positiveGarageId = Guid.NewGuid();
        var creditGarageId = Guid.NewGuid();
        var createdAtUtc = DateTimeOffset.UtcNow;

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "garages"
                    ("Id", "Number", "PeopleCount", "FloorCount", "StartingBalance", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({positiveGarageId}, {"Миграция-долг"}, 1, 1, {125.50m}, FALSE, {createdAtUtc}, {createdAtUtc}),
                    ({creditGarageId}, {"Миграция-аванс"}, 1, 1, {-50m}, FALSE, {createdAtUtc}, {createdAtUtc});
                """);
            await setupContext.Database.MigrateAsync();
        }

        await using (var verificationContext = database.CreateContext())
        {
            var positiveGarage = await verificationContext.Garages.FindAsync(positiveGarageId);
            var creditGarage = await verificationContext.Garages.FindAsync(creditGarageId);

            Assert.Equal(125.50m, positiveGarage!.StartingOverdueDebt);
            Assert.Equal(0m, creditGarage!.StartingOverdueDebt);
        }

        await using var invalidContext = database.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            invalidContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "garages"
                SET "StartingOverdueDebt" = {126m}
                WHERE "Id" = {positiveGarageId};
                """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
