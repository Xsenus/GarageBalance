using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedDemoTariffCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM tariffs)
                       AND NOT EXISTS (SELECT 1 FROM charge_service_settings)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'water' AND "IsArchived" = FALSE)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'electricity' AND "IsArchived" = FALSE)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'membership' AND "IsArchived" = FALSE)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'target' AND "IsArchived" = FALSE)
                    THEN
                        INSERT INTO tariffs (
                            "Id", "Name", "CalculationBase", "Rate", "EffectiveFrom", "Comment", "IsArchived",
                            "CreatedAtUtc", "UpdatedAtUtc", "ElectricityFirstThreshold", "ElectricitySecondThreshold",
                            "ElectricityFirstTierName", "ElectricitySecondTierName", "ElectricityThirdTierName",
                            "ElectricityFirstRate", "ElectricitySecondRate", "ElectricityThirdRate")
                        VALUES
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185101', 'Тариф на воду', 'meter_water', 45.0000,
                                DATE '2026-01-01', 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185102', 'Электроэнергия', 'meter_electricity', 6.2000,
                                DATE '2026-01-01', 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                1.0000, 3.0000, 'От 0 кВт', 'От 1 кВт', 'От 3 кВт', 6.2000, 6.2000, 6.2000
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185103', 'Сумма членского взноса', 'fixed', 500.0000,
                                DATE '2026-01-01', 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185104', 'Сумма целевого взноса', 'fixed', 1200.0000,
                                DATE '2026-07-01', 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            );

                        INSERT INTO charge_service_settings (
                            "Id", "Name", "IsRegular", "PeriodicityMonths", "AccrualStartMonth", "PaymentDueDay",
                            "PaymentDueMonth", "OverdueGraceDays", "IncomeTypeId", "TariffId", "IsMetered",
                            "HasTieredTariff", "UnitName", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                        VALUES
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287101', 'Вода', TRUE, 1, 1, NULL, NULL, 30,
                                (SELECT "Id" FROM income_types WHERE "Code" = 'water' AND "IsArchived" = FALSE LIMIT 1),
                                '8a92bf70-9339-4bbc-8e5d-a05cda185101', TRUE, FALSE, 'м³', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            ),
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287102', 'Электроэнергия', TRUE, 1, 1, NULL, NULL, 30,
                                (SELECT "Id" FROM income_types WHERE "Code" = 'electricity' AND "IsArchived" = FALSE LIMIT 1),
                                '8a92bf70-9339-4bbc-8e5d-a05cda185102', TRUE, TRUE, 'кВт·ч', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            ),
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287103', 'Членский взнос', TRUE, 12, 1, 30, 6, 30,
                                (SELECT "Id" FROM income_types WHERE "Code" = 'membership' AND "IsArchived" = FALSE LIMIT 1),
                                '8a92bf70-9339-4bbc-8e5d-a05cda185103', FALSE, FALSE, 'руб.', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            ),
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287104', 'Целевой взнос', TRUE, 12, 1, 30, 6, 30,
                                (SELECT "Id" FROM income_types WHERE "Code" = 'target' AND "IsArchived" = FALSE LIMIT 1),
                                '8a92bf70-9339-4bbc-8e5d-a05cda185104', FALSE, FALSE, 'руб.', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            );

                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '55af4e19-59d8-4b2e-99f8-744412b16285', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                            'dictionary.demo_tariff_catalog_seeded', 'dictionary', 'create', 'demo_tariff_catalog',
                            'stage-8', 'Демонстрационные тарифы',
                            'Добавлены демонстрационные тарифы и настройки услуг для показа пустой установки.',
                            '{"source":"docs/stage-8-demo-test-data.json","tariffCount":4,"serviceCount":4}'
                        );
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM charge_service_settings
                WHERE "Id" IN (
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287101',
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287102',
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287103',
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287104'
                );

                DELETE FROM tariffs
                WHERE "Id" IN (
                    '8a92bf70-9339-4bbc-8e5d-a05cda185101',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185102',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185103',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185104'
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM accruals
                    WHERE accruals."TariffId" = tariffs."Id"
                );

                DELETE FROM audit_events
                WHERE "Id" = '55af4e19-59d8-4b2e-99f8-744412b16285';
                """);
        }
    }
}
