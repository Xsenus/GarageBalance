using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileGelendzhikTariffCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE garagebalance_regulated_tariff_catalog (
                    tariff_id uuid PRIMARY KEY,
                    service_code text NOT NULL,
                    tariff_name text NOT NULL,
                    calculation_base text NOT NULL,
                    rate numeric(18,4) NOT NULL,
                    effective_from date NOT NULL,
                    effective_to date NULL,
                    first_threshold numeric(18,4) NULL,
                    second_threshold numeric(18,4) NULL,
                    first_rate numeric(18,4) NULL,
                    second_rate numeric(18,4) NULL,
                    third_rate numeric(18,4) NULL,
                    source_comment text NOT NULL
                ) ON COMMIT DROP;

                INSERT INTO garagebalance_regulated_tariff_catalog VALUES
                    ('d0010000-0000-4000-8000-000000000001', 'water', 'Вода — 01.01.2020', 'meter_water', 56.32, '2020-01-01', '2020-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы №167 в действовавшей редакции.'),
                    ('d0010000-0000-4000-8000-000000000002', 'water', 'Вода — 01.07.2020', 'meter_water', 60.41, '2020-07-01', '2020-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы №167 в действовавшей редакции.'),
                    ('d0010000-0000-4000-8000-000000000003', 'water', 'Вода — 01.01.2021', 'meter_water', 60.41, '2021-01-01', '2021-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы №167 в действовавшей редакции.'),
                    ('d0010000-0000-4000-8000-000000000004', 'water', 'Вода — 01.07.2021', 'meter_water', 65.24, '2021-07-01', '2021-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 17.12.2021 №445.'),
                    ('d0010000-0000-4000-8000-000000000005', 'water', 'Вода — 01.01.2022', 'meter_water', 65.24, '2022-01-01', '2022-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 17.12.2021 №445.'),
                    ('d0010000-0000-4000-8000-000000000006', 'water', 'Вода — 01.07.2022', 'meter_water', 71.64, '2022-07-01', '2022-11-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 17.12.2021 №445.'),
                    ('d0010000-0000-4000-8000-000000000007', 'water', 'Вода — 01.12.2022', 'meter_water', 79.54, '2022-12-01', '2023-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 29.11.2022 №556.'),
                    ('d0010000-0000-4000-8000-000000000008', 'water', 'Вода — 01.01.2024', 'meter_water', 79.54, '2024-01-01', '2024-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2023 №25.'),
                    ('d0010000-0000-4000-8000-000000000009', 'water', 'Вода — 01.07.2024', 'meter_water', 88.28, '2024-07-01', '2024-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2023 №25.'),
                    ('d0010000-0000-4000-8000-000000000010', 'water', 'Вода — 01.01.2025', 'meter_water', 88.28, '2025-01-01', '2025-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2024 №180.'),
                    ('d0010000-0000-4000-8000-000000000011', 'water', 'Вода — 01.07.2025', 'meter_water', 98.95, '2025-07-01', '2025-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2024 №180.'),
                    ('d0010000-0000-4000-8000-000000000012', 'water', 'Вода — 01.01.2026', 'meter_water', 100.60, '2026-01-01', '2026-09-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),
                    ('d0010000-0000-4000-8000-000000000013', 'water', 'Вода — 01.10.2026', 'meter_water', 112.48, '2026-10-01', '2026-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),
                    ('d0010000-0000-4000-8000-000000000014', 'water', 'Вода — 01.01.2027', 'meter_water', 112.48, '2027-01-01', '2027-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),
                    ('d0010000-0000-4000-8000-000000000015', 'water', 'Вода — 01.07.2027', 'meter_water', 118.71, '2027-07-01', '2027-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),
                    ('d0010000-0000-4000-8000-000000000016', 'water', 'Вода — 01.01.2028', 'meter_water', 118.71, '2028-01-01', '2028-06-30', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),
                    ('d0010000-0000-4000-8000-000000000017', 'water', 'Вода — 01.07.2028', 'meter_water', 122.37, '2028-07-01', '2028-12-31', NULL, NULL, NULL, NULL, NULL, 'Геленджик, питьевая вода для населения с НДС; решение Думы от 19.12.2025 №304.'),

                    ('e0010000-0000-4000-8000-000000000001', 'electricity', 'Электроэнергия — 01.01.2020', 'meter_electricity', 4.81, '2020-01-01', '2020-06-30', 11000, 20000, 4.81, 4.81, 4.81, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; АО «НЭСК», 2020.'),
                    ('e0010000-0000-4000-8000-000000000002', 'electricity', 'Электроэнергия — 01.07.2020', 'meter_electricity', 5.02, '2020-07-01', '2020-12-31', 11000, 20000, 5.02, 5.02, 5.02, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; АО «НЭСК», 2020.'),
                    ('e0010000-0000-4000-8000-000000000003', 'electricity', 'Электроэнергия — 01.01.2021', 'meter_electricity', 5.02, '2021-01-01', '2021-06-30', 11000, 20000, 5.02, 5.02, 5.02, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; приказ №41/2020-э.'),
                    ('e0010000-0000-4000-8000-000000000004', 'electricity', 'Электроэнергия — 01.07.2021', 'meter_electricity', 5.24, '2021-07-01', '2021-12-31', 11000, 20000, 5.24, 5.24, 5.24, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; приказ №41/2020-э.'),
                    ('e0010000-0000-4000-8000-000000000005', 'electricity', 'Электроэнергия — 01.01.2022', 'meter_electricity', 5.24, '2022-01-01', '2022-06-30', 11000, 20000, 5.24, 5.24, 5.24, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; АО «НЭСК», 2022.'),
                    ('e0010000-0000-4000-8000-000000000006', 'electricity', 'Электроэнергия — 01.07.2022', 'meter_electricity', 5.50, '2022-07-01', '2022-11-30', 11000, 20000, 5.50, 5.50, 5.50, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; АО «НЭСК», 2022.'),
                    ('e0010000-0000-4000-8000-000000000007', 'electricity', 'Электроэнергия — 01.12.2022', 'meter_electricity', 6.00, '2022-12-01', '2023-12-31', 11000, 20000, 6.00, 6.00, 6.00, 'Краснодарский край, одноставочный тариф для населения и приравненных категорий; приказ №32/2022-э.'),
                    ('e0010000-0000-4000-8000-000000000008', 'electricity', 'Электроэнергия — 01.01.2024', 'meter_electricity', 6.00, '2024-01-01', '2024-06-30', 11000, 20000, 6.00, 6.00, 6.00, 'Краснодарский край, единая ставка до введения диапазонов; приказ №27/2023-э.'),
                    ('e0010000-0000-4000-8000-000000000009', 'electricity', 'Электроэнергия — 01.07.2024', 'meter_electricity', 6.53, '2024-07-01', '2024-12-31', 11000, 20000, 6.53, 6.54, 9.50, 'Краснодарский край, ГСК на один гараж: диапазоны 11000/20000 кВт·ч; приказ №27/2023-э.'),
                    ('e0010000-0000-4000-8000-000000000010', 'electricity', 'Электроэнергия — 01.01.2025', 'meter_electricity', 6.53, '2025-01-01', '2025-06-30', 100, 155, 6.53, 6.54, 9.50, 'Краснодарский край, ГСК на один гараж: диапазоны 100/155 кВт·ч; приказ №24/2024-э.'),
                    ('e0010000-0000-4000-8000-000000000011', 'electricity', 'Электроэнергия — 01.07.2025', 'meter_electricity', 7.35, '2025-07-01', '2025-12-31', 100, 155, 7.35, 10.00, 14.64, 'Краснодарский край, ГСК на один гараж: диапазоны 100/155 кВт·ч; приказ №24/2024-э.'),
                    ('e0010000-0000-4000-8000-000000000012', 'electricity', 'Электроэнергия — 01.01.2026', 'meter_electricity', 7.47, '2026-01-01', '2026-09-30', 100, 155, 7.47, 10.17, 14.88, 'Краснодарский край, ГСК на один гараж: диапазоны 100/155 кВт·ч; приказ №18/2025-э.'),
                    ('e0010000-0000-4000-8000-000000000013', 'electricity', 'Электроэнергия — 01.10.2026', 'meter_electricity', 8.31, '2026-10-01', '2026-12-31', 100, 155, 8.31, 11.70, 17.84, 'Краснодарский край, ГСК на один гараж: диапазоны 100/155 кВт·ч; приказ №18/2025-э.'),

                    ('a0010000-0000-4000-8000-000000000001', 'trash', 'Мусор — 01.01.2020', 'people', 123.30, '2020-01-01', '2020-08-31', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД: 591,83 руб./м³ × 2,5 м³/год ÷ 12; приказ №41/2019-тко.'),
                    ('a0010000-0000-4000-8000-000000000002', 'trash', 'Мусор — 01.09.2020', 'people', 122.65, '2020-09-01', '2022-06-30', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД: 588,70 руб./м³ × 2,5 м³/год ÷ 12; приказы №6/2020-тко и №29/2020-тко.'),
                    ('a0010000-0000-4000-8000-000000000003', 'trash', 'Мусор — 01.07.2022', 'people', 125.00, '2022-07-01', '2022-11-30', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД: 600 руб./м³ × 2,5 м³/год ÷ 12; приказ №27/2021-тко.'),
                    ('a0010000-0000-4000-8000-000000000004', 'trash', 'Мусор — 01.12.2022', 'people', 118.82, '2022-12-01', '2023-12-31', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД: 570,31 руб./м³ × 2,5 м³/год ÷ 12; приказ №22/2022-тко.'),
                    ('a0010000-0000-4000-8000-000000000005', 'trash', 'Мусор — 01.01.2024', 'people', 108.15, '2024-01-01', '2025-05-31', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД: 519,11 руб./м³ × 2,5 м³/год ÷ 12; приказ №20/2023-тко.'),
                    ('a0010000-0000-4000-8000-000000000006', 'trash', 'Мусор — 01.06.2025', 'people', 108.15, '2025-06-01', '2025-06-30', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД, ООО «Южный региональный оператор»: 519,11 руб./м³; приказ №1/2025-тко.'),
                    ('a0010000-0000-4000-8000-000000000007', 'trash', 'Мусор — 01.07.2025', 'people', 126.53, '2025-07-01', '2025-12-31', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД, ООО «Южный региональный оператор»: 607,36 руб./м³ × 2,5 м³/год ÷ 12; приказ №1/2025-тко.'),
                    ('a0010000-0000-4000-8000-000000000008', 'trash', 'Мусор — 01.01.2026', 'people', 128.69, '2026-01-01', '2026-09-30', NULL, NULL, NULL, NULL, NULL, 'Новороссийская зона, МКД, ООО «Южный региональный оператор»: 617,69 руб./м³ × 2,5 м³/год ÷ 12; приказ ДГРТ от 19.12.2025.');

                INSERT INTO tariffs (
                    "Id", "Name", "CalculationBase", "Rate", "EffectiveFrom", "Comment", "IsArchived",
                    "CreatedAtUtc", "UpdatedAtUtc", "ElectricityFirstThreshold", "ElectricitySecondThreshold",
                    "ElectricityFirstTierName", "ElectricitySecondTierName", "ElectricityThirdTierName",
                    "ElectricityFirstRate", "ElectricitySecondRate", "ElectricityThirdRate", "ElectricityTiersJson")
                SELECT
                    tariff_id, tariff_name, calculation_base, rate, effective_from, source_comment, FALSE,
                    TIMESTAMPTZ '2026-09-08T05:32:53Z', TIMESTAMPTZ '2026-09-08T05:32:53Z',
                    first_threshold, second_threshold,
                    CASE WHEN first_threshold IS NULL THEN NULL ELSE 'До ' || first_threshold::text || ' кВт·ч' END,
                    CASE WHEN second_threshold IS NULL THEN NULL ELSE 'До ' || second_threshold::text || ' кВт·ч' END,
                    CASE WHEN second_threshold IS NULL THEN NULL ELSE 'Свыше ' || second_threshold::text || ' кВт·ч' END,
                    first_rate, second_rate, third_rate, NULL
                FROM garagebalance_regulated_tariff_catalog
                ON CONFLICT ("Id") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "CalculationBase" = EXCLUDED."CalculationBase",
                    "Rate" = EXCLUDED."Rate",
                    "EffectiveFrom" = EXCLUDED."EffectiveFrom",
                    "Comment" = EXCLUDED."Comment",
                    "IsArchived" = FALSE,
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc",
                    "ElectricityFirstThreshold" = EXCLUDED."ElectricityFirstThreshold",
                    "ElectricitySecondThreshold" = EXCLUDED."ElectricitySecondThreshold",
                    "ElectricityFirstTierName" = EXCLUDED."ElectricityFirstTierName",
                    "ElectricitySecondTierName" = EXCLUDED."ElectricitySecondTierName",
                    "ElectricityThirdTierName" = EXCLUDED."ElectricityThirdTierName",
                    "ElectricityFirstRate" = EXCLUDED."ElectricityFirstRate",
                    "ElectricitySecondRate" = EXCLUDED."ElectricitySecondRate",
                    "ElectricityThirdRate" = EXCLUDED."ElectricityThirdRate",
                    "ElectricityTiersJson" = NULL;

                DELETE FROM charge_service_tariff_versions AS version
                USING charge_service_settings AS service, income_types AS income_type
                WHERE version."ChargeServiceSettingId" = service."Id"
                  AND service."IncomeTypeId" = income_type."Id"
                  AND income_type."Code" IN ('water', 'electricity', 'trash');

                INSERT INTO charge_service_tariff_versions (
                    "ChargeServiceSettingId", "EffectiveFrom", "EffectiveTo", "IsArchived", "TariffId", "CreatedAtUtc")
                SELECT service."Id", catalog.effective_from, catalog.effective_to, FALSE, catalog.tariff_id,
                       TIMESTAMPTZ '2026-09-08T05:32:53Z'
                FROM garagebalance_regulated_tariff_catalog AS catalog
                INNER JOIN income_types AS income_type ON income_type."Code" = catalog.service_code
                INNER JOIN charge_service_settings AS service ON service."IncomeTypeId" = income_type."Id";

                UPDATE charge_service_settings AS service
                SET "TariffId" = current_tariff.tariff_id,
                    "HasTieredTariff" = current_tariff.service_code = 'electricity',
                    "IsMetered" = current_tariff.service_code IN ('water', 'electricity'),
                    "MeterKind" = CASE current_tariff.service_code WHEN 'water' THEN 'water' WHEN 'electricity' THEN 'electricity' ELSE NULL END,
                    "UnitName" = CASE current_tariff.service_code WHEN 'water' THEN 'м³' WHEN 'electricity' THEN 'кВт·ч' ELSE 'чел.' END,
                    "UpdatedAtUtc" = TIMESTAMPTZ '2026-09-08T05:32:53Z'
                FROM garagebalance_regulated_tariff_catalog AS current_tariff
                INNER JOIN income_types AS income_type ON income_type."Code" = current_tariff.service_code
                WHERE service."IncomeTypeId" = income_type."Id"
                  AND DATE '2026-09-08' BETWEEN current_tariff.effective_from AND COALESCE(current_tariff.effective_to, DATE '9999-12-31');

                INSERT INTO audit_events (
                    "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                    "EntityDisplayName", "Summary", "MetadataJson")
                SELECT
                    '921e4697-413b-43a1-9b95-f397443e753e', TIMESTAMPTZ '2026-09-08T05:32:53Z',
                    'dictionary.regulated_tariff_history_reconciled', 'dictionary', 'update', 'tariff_catalog',
                    'gelendzhik-2020-2028', 'Регулируемые тарифы Геленджика',
                    'Тарифные периоды воды, электроэнергии и вывоза мусора сверены с опубликованными решениями; услуги и тарифные записи сохранены.',
                    '{"waterPeriods":17,"electricityPeriods":13,"trashPeriods":8,"electricityGarageThresholds":[100,155],"tariffsDeleted":0}'
                WHERE EXISTS (
                    SELECT 1 FROM charge_service_settings AS service
                    INNER JOIN income_types AS income_type ON income_type."Id" = service."IncomeTypeId"
                    WHERE income_type."Code" IN ('water', 'electricity', 'trash'))
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The reconciliation preserves legacy tariff rows and is intentionally not reversed:
            // rolling it back would reintroduce incorrect production prices and period overlaps.
        }
    }
}
