using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlTariffTemplateMigrationIntegrationTests
{
    private const string PreviousMigration = "20260811204719_MoveSupplierExpenseSettingsOutOfChargeServices";

    [PostgreSqlFact]
    public async Task Migration_RemovesTemplateClassificationAndPreservesEveryTariffRecord()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var legacyTemplateId = Guid.NewGuid();
        var serviceVersionId = Guid.NewGuid();

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tariffs
                    ("Id", "Name", "CalculationBase", "Rate", "EffectiveFrom", "IsTemplate",
                     "IsArchived", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                VALUES
                    ({legacyTemplateId}, {"Старый шаблон тарифа"}, {"fixed"}, {100m},
                     {new DateOnly(2026, 8, 1)}, TRUE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid()),
                    ({serviceVersionId}, {"Историческая версия услуги"}, {"meter_water"}, {25m},
                     {new DateOnly(2026, 8, 2)}, FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, gen_random_uuid());
                """);

            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var retainedIds = await verificationContext.Tariffs
            .Where(tariff => tariff.Id == legacyTemplateId || tariff.Id == serviceVersionId)
            .Select(tariff => tariff.Id)
            .ToArrayAsync();
        Assert.Contains(legacyTemplateId, retainedIds);
        Assert.Contains(serviceVersionId, retainedIds);

        var templateColumnCount = await verificationContext.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::integer AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'tariffs'
                  AND column_name = 'IsTemplate'
                """)
            .SingleAsync();
        Assert.Equal(0, templateColumnCount);
    }
}
