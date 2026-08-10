using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlTariffTemplateMigrationIntegrationTests
{
    private const string PreviousMigration = "20260805065345_ApplyAutomaticIncomeFundBalances";
    private const string CleanupPreviousMigration = "20260810103955_DistinguishTariffTemplates";

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
            await migrateContext.GetService<IMigrator>().MigrateAsync(CleanupPreviousMigration);
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

    [PostgreSqlFact]
    public async Task Migration_RemovesOnlyUnreferencedGeneratedTariffVersions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var orphan = new Tariff
        {
            Name = "Вода — по счетчику, 10.08.2026, 1234abcd",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 10),
            IsTemplate = false
        };
        var current = new Tariff
        {
            Name = "Вода — по счетчику",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 8, 10),
            IsTemplate = false
        };
        var historical = new Tariff
        {
            Name = "Вода — обычный",
            CalculationBase = "fixed",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsTemplate = false
        };
        var template = new Tariff
        {
            Name = "Шаблон воды",
            CalculationBase = "meter_water",
            Rate = 100.80m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsTemplate = true
        };
        var service = new ChargeServiceSetting
        {
            Name = "Тестовая услуга очистки версий тарифа",
            IsRegular = true,
            PeriodicityMonths = 1,
            AccrualStartMonth = 1,
            PaymentDueDay = 30,
            OverdueGraceDays = 30,
            TariffId = current.Id,
            IsMetered = true,
            UnitName = "м³"
        };

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(CleanupPreviousMigration);
            setupContext.AddRange(orphan, current, historical, template, service);
            setupContext.ChargeServiceTariffVersions.AddRange(
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = service.Id,
                    TariffId = historical.Id,
                    EffectiveFrom = historical.EffectiveFrom
                },
                new ChargeServiceTariffVersion
                {
                    ChargeServiceSettingId = service.Id,
                    TariffId = current.Id,
                    EffectiveFrom = current.EffectiveFrom
                });
            await setupContext.SaveChangesAsync();
        }

        await using (var migrateContext = database.CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        var retainedIds = await verificationContext.Tariffs
            .Where(item => item.Id == orphan.Id || item.Id == current.Id || item.Id == historical.Id || item.Id == template.Id)
            .Select(item => item.Id)
            .ToArrayAsync();

        Assert.DoesNotContain(orphan.Id, retainedIds);
        Assert.Contains(current.Id, retainedIds);
        Assert.Contains(historical.Id, retainedIds);
        Assert.Contains(template.Id, retainedIds);
    }
}
