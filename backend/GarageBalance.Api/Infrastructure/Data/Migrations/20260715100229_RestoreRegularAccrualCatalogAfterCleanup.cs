using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreRegularAccrualCatalogAfterCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    changed_rows integer := 0;
                    inserted_rows integer := 0;
                BEGIN
                    WITH defaults("Id", "Name", "Code") AS (
                        VALUES
                            ('3e79c525-377d-4168-b8de-f2ff736829df'::uuid, 'Вода', 'water'),
                            ('a55bf70e-3f02-42f5-9ac3-e4ac7278b1b0'::uuid, 'Мусор', 'trash'),
                            ('98afc64c-2d57-4276-bc7f-58f0de6ce92e'::uuid, 'Электроэнергия', 'electricity'),
                            ('f465d904-2b57-4e99-bd88-1bd04b2f6e66'::uuid, 'Членский взнос', 'membership'),
                            ('cfdbb49e-e6c8-4b91-8a10-f783cd55b4c8'::uuid, 'Целевой взнос', 'target'),
                            ('19b731c2-43a6-4dcd-9670-ba76a512b091'::uuid, 'Вступительный взнос', 'entry'),
                            ('95f18e62-7ac8-4aac-b80e-af371a6d9581'::uuid, 'Подключения', 'connection'),
                            ('9e78f409-6a41-4073-a8af-8d5b4b80d85b'::uuid, 'Штраф', 'penalty'),
                            ('13965d6a-e589-42af-bf29-f44c704bdd61'::uuid, 'Предписание', 'notice')
                    )
                    INSERT INTO income_types (
                        "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        defaults."Id", defaults."Name", defaults."Code", TRUE, FALSE,
                        TIMESTAMPTZ '2026-07-15T10:02:29Z', TIMESTAMPTZ '2026-07-15T10:02:29Z'
                    FROM defaults
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM income_types existing
                        WHERE existing."Id" = defaults."Id"
                           OR LOWER(BTRIM(existing."Name")) = LOWER(BTRIM(defaults."Name"))
                           OR LOWER(BTRIM(existing."Code")) = LOWER(BTRIM(defaults."Code"))
                    )
                    ON CONFLICT DO NOTHING;

                    GET DIAGNOSTICS inserted_rows = ROW_COUNT;
                    changed_rows := changed_rows + inserted_rows;

                    WITH defaults(
                        "Id", "Name", "PeriodicityMonths", "AccrualStartMonth", "PaymentDueDay",
                        "PaymentDueMonth", "OverdueGraceDays", "IncomeTypeCode", "TariffId",
                        "IsMetered", "HasTieredTariff", "UnitName") AS (
                        VALUES
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287101'::uuid, 'Вода', 1, 1, NULL::integer, NULL::integer, 30, 'water', '8a92bf70-9339-4bbc-8e5d-a05cda185101'::uuid, TRUE, FALSE, 'м³'),
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287102'::uuid, 'Электроэнергия', 1, 1, NULL::integer, NULL::integer, 30, 'electricity', '8a92bf70-9339-4bbc-8e5d-a05cda185102'::uuid, TRUE, TRUE, 'кВт·ч'),
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287103'::uuid, 'Членский взнос', 12, 1, 30, 6, 30, 'membership', '8a92bf70-9339-4bbc-8e5d-a05cda185103'::uuid, FALSE, FALSE, 'руб.'),
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287104'::uuid, 'Целевой взнос', 12, 1, 30, 6, 30, 'target', '8a92bf70-9339-4bbc-8e5d-a05cda185104'::uuid, FALSE, FALSE, 'руб.'),
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287105'::uuid, 'Мусор', 1, 1, NULL::integer, NULL::integer, 30, 'trash', '8a92bf70-9339-4bbc-8e5d-a05cda185105'::uuid, FALSE, FALSE, 'чел.'),
                            ('f0d7ed2e-ec55-42b4-8a79-01b37c287106'::uuid, 'Наружное освещение', 12, 1, 31, 12, 0, NULL, '8a92bf70-9339-4bbc-8e5d-a05cda185106'::uuid, FALSE, FALSE, 'руб.')
                    )
                    INSERT INTO charge_service_settings (
                        "Id", "Name", "IsRegular", "PeriodicityMonths", "AccrualStartMonth", "PaymentDueDay",
                        "PaymentDueMonth", "OverdueGraceDays", "IncomeTypeId", "TariffId", "IsMetered",
                        "HasTieredTariff", "UnitName", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        defaults."Id", defaults."Name", TRUE, defaults."PeriodicityMonths",
                        defaults."AccrualStartMonth", defaults."PaymentDueDay", defaults."PaymentDueMonth",
                        defaults."OverdueGraceDays", income_type."Id", defaults."TariffId",
                        defaults."IsMetered", defaults."HasTieredTariff", defaults."UnitName", FALSE,
                        TIMESTAMPTZ '2026-07-15T10:02:29Z', TIMESTAMPTZ '2026-07-15T10:02:29Z'
                    FROM defaults
                    INNER JOIN tariffs tariff
                        ON tariff."Id" = defaults."TariffId" AND tariff."IsArchived" = FALSE
                    LEFT JOIN income_types income_type
                        ON LOWER(BTRIM(income_type."Code")) = LOWER(BTRIM(defaults."IncomeTypeCode"))
                       AND income_type."IsArchived" = FALSE
                    WHERE (defaults."IncomeTypeCode" IS NULL OR income_type."Id" IS NOT NULL)
                      AND NOT EXISTS (
                          SELECT 1
                          FROM charge_service_settings existing
                          WHERE existing."Id" = defaults."Id"
                             OR LOWER(BTRIM(existing."Name")) = LOWER(BTRIM(defaults."Name"))
                      )
                    ON CONFLICT DO NOTHING;

                    GET DIAGNOSTICS inserted_rows = ROW_COUNT;
                    changed_rows := changed_rows + inserted_rows;

                    IF changed_rows > 0 THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '12e23ef4-67f3-48ae-b829-0a557a1259b1', TIMESTAMPTZ '2026-07-15T10:02:29Z',
                            'dictionary.regular_accrual_catalog_restored', 'dictionary', 'create',
                            'regular_accrual_catalog', 'system-defaults', 'Регулярные начисления',
                            'Восстановлены отсутствующие системные виды поступлений и настройки регулярных услуг без изменения существующих тарифов и пользовательских настроек.',
                            jsonb_build_object('changedRowCount', changed_rows, 'reason', 'partial-catalog-cleanup')::text
                        )
                        ON CONFLICT ("Id") DO NOTHING;
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
                    'f0d7ed2e-ec55-42b4-8a79-01b37c287106')
                  AND "CreatedAtUtc" = TIMESTAMPTZ '2026-07-15T10:02:29Z'
                  AND "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-15T10:02:29Z';

                DELETE FROM income_types
                WHERE "Id" IN (
                    '3e79c525-377d-4168-b8de-f2ff736829df',
                    'a55bf70e-3f02-42f5-9ac3-e4ac7278b1b0',
                    '98afc64c-2d57-4276-bc7f-58f0de6ce92e',
                    'f465d904-2b57-4e99-bd88-1bd04b2f6e66',
                    'cfdbb49e-e6c8-4b91-8a10-f783cd55b4c8',
                    '19b731c2-43a6-4dcd-9670-ba76a512b091',
                    '95f18e62-7ac8-4aac-b80e-af371a6d9581',
                    '9e78f409-6a41-4073-a8af-8d5b4b80d85b',
                    '13965d6a-e589-42af-bf29-f44c704bdd61')
                  AND "CreatedAtUtc" = TIMESTAMPTZ '2026-07-15T10:02:29Z'
                  AND "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-15T10:02:29Z'
                  AND NOT EXISTS (
                      SELECT 1 FROM charge_service_settings WHERE charge_service_settings."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM accruals WHERE accruals."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM financial_operations WHERE financial_operations."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM fee_campaigns WHERE fee_campaigns."IncomeTypeId" = income_types."Id");

                DELETE FROM audit_events
                WHERE "Id" = '12e23ef4-67f3-48ae-b829-0a557a1259b1';
                """);
        }
    }
}
