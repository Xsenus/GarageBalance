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
                       AND NOT EXISTS (SELECT 1 FROM irregular_payments)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'water' AND "IsArchived" = FALSE)
                       AND EXISTS (SELECT 1 FROM income_types WHERE "Code" = 'trash' AND "IsArchived" = FALSE)
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
                                '8a92bf70-9339-4bbc-8e5d-a05cda185101', 'Тариф на воду', 'meter_water', 100.6000,
                                DATE '2026-01-01', 'Геленджик, 01.01-30.09.2026: питьевая вода для населения, решение Думы от 19.12.2025 № 304.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185102', 'Электроэнергия', 'meter_electricity', 7.4700,
                                DATE '2026-01-01', 'Краснодарский край, 01.01-30.09.2026: город без электроплит, приказ ДГРТ от 17.12.2025 № 18/2025-э.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                1100.0000, 1700.0000, 'До 1 100 кВт·ч', 'От 1 100 до 1 700 кВт·ч', 'Свыше 1 700 кВт·ч', 7.4700, 10.1700, 14.8800
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
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185105', 'Ставка за вывоз мусора', 'people', 128.6900,
                                DATE '2026-01-01', 'Геленджик, 01.01-30.09.2026: ежемесячная плата с человека в МКД, Новороссийская зона ООО «Южный региональный оператор».',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185106', 'Наружное освещение', 'fixed', 300.0000,
                                DATE '2026-01-01', 'Демонстрационные данные из формы ГСК. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185107', 'Электрики', 'fixed', 500.0000,
                                DATE '2026-01-01', 'Демонстрационные данные из формы ГСК. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185108', 'Бухгалтерия', 'fixed', 700.0000,
                                DATE '2026-01-01', 'Демонстрационные данные из формы ГСК. Перед реальным учетом проверьте и замените ставку.',
                                FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                                NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                            ),
                            (
                                '8a92bf70-9339-4bbc-8e5d-a05cda185109', 'Руководство', 'fixed', 900.0000,
                                DATE '2026-01-01', 'Демонстрационные данные из формы ГСК. Перед реальным учетом проверьте и замените ставку.',
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
                            ),
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287105', 'Мусор', TRUE, 1, 1, NULL, NULL, 30,
                                (SELECT "Id" FROM income_types WHERE "Code" = 'trash' AND "IsArchived" = FALSE LIMIT 1),
                                '8a92bf70-9339-4bbc-8e5d-a05cda185105', FALSE, FALSE, 'чел.', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            ),
                            (
                                'f0d7ed2e-ec55-42b4-8a79-01b37c287106', 'Наружное освещение', TRUE, 12, 1, 31, 12, 0,
                                NULL, '8a92bf70-9339-4bbc-8e5d-a05cda185106', FALSE, FALSE, 'руб.', FALSE,
                                TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'
                            );

                        INSERT INTO irregular_payments (
                            "Id", "Name", "Amount", "IsActive", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                        VALUES
                            ('c865fd0a-ae14-4de6-83ef-b5d692327101', 'Вступительный взнос', 5000.00, TRUE, FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'),
                            ('c865fd0a-ae14-4de6-83ef-b5d692327102', 'Подключение канализации', 10000.00, TRUE, FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'),
                            ('c865fd0a-ae14-4de6-83ef-b5d692327103', 'Подключение линии электросети', 15000.00, TRUE, FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z');

                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '55af4e19-59d8-4b2e-99f8-744412b16285', TIMESTAMPTZ '2026-07-13T06:52:13Z',
                            'dictionary.demo_tariff_catalog_seeded', 'dictionary', 'create', 'demo_tariff_catalog',
                            'stage-8', 'Демонстрационные тарифы',
                            'Добавлены демонстрационные тарифы, настройки услуг и нерегулярные платежи для показа пустой установки.',
                            '{"sources":["customer tariff form screenshot","Gelendzhik municipal decision 304/2025","Krasnodar DGRТ order 18/2025-e","Southern regional operator tariff 2026"],"tariffCount":9,"serviceCount":6,"irregularPaymentCount":3}'
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
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287104',
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287105',
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287106'
                );

                DELETE FROM irregular_payments
                WHERE "Id" IN (
                    'c865fd0a-ae14-4de6-83ef-b5d692327101',
                    'c865fd0a-ae14-4de6-83ef-b5d692327102',
                    'c865fd0a-ae14-4de6-83ef-b5d692327103',
                    'c865fd0a-ae14-4de6-83ef-b5d692327104',
                    'c865fd0a-ae14-4de6-83ef-b5d692327105'
                );

                DELETE FROM tariffs
                WHERE "Id" IN (
                    '8a92bf70-9339-4bbc-8e5d-a05cda185101',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185102',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185103',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185104',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185105',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185106',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185107',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185108',
                    '8a92bf70-9339-4bbc-8e5d-a05cda185109'
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
