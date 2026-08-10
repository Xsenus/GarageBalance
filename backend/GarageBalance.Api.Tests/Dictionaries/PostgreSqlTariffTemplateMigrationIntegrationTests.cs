using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlTariffTemplateMigrationIntegrationTests
{
    private const string PreviousMigration = "20260805065345_ApplyAutomaticIncomeFundBalances";

    [PostgreSqlFact]
    public async Task Migration_SeparatesDictionaryTemplatesFromGeneratedServiceVersions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var downgradeContext = database.CreateContext())
        {
            await downgradeContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await downgradeContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO tariffs
                    ("Id", "Name", "CalculationBase", "Rate", "EffectiveFrom", "IsArchived", "Comment", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                VALUES
                    ('10000000-0000-0000-0000-000000000001', 'Вода — по счетчику, 05.08.2026, abcdef12', 'meter_water', 100.80, DATE '2026-08-05', FALSE, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid()),
                    ('10000000-0000-0000-0000-000000000002', 'Шаблон тарифа воды', 'meter_water', 100.80, DATE '2026-08-01', FALSE, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid()),
                    ('10000000-0000-0000-0000-000000000003', 'Вода — тариф', 'fixed', 100.80, DATE '2026-08-01', FALSE, 'Создан вместе с услугой «Вода».', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid());
                """);
        }

        await using (var migrateContext = database.CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var classifications = await verificationContext.Tariffs
            .Where(item => item.Id == Guid.Parse("10000000-0000-0000-0000-000000000001") ||
                           item.Id == Guid.Parse("10000000-0000-0000-0000-000000000002") ||
                           item.Id == Guid.Parse("10000000-0000-0000-0000-000000000003"))
            .OrderBy(item => item.Id)
            .Select(item => item.IsTemplate)
            .ToArrayAsync();

        Assert.Equal(new[] { false, true, false }, classifications);
    }
}
