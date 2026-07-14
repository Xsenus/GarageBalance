using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ApplyGelendzhik2026TariffDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    changed_rows integer := 0;
                    affected_rows integer := 0;
                BEGIN
                    UPDATE tariffs
                    SET "Rate" = 100.6000,
                        "Comment" = 'Геленджик, 01.01-30.09.2026: питьевая вода для населения, решение Думы от 19.12.2025 № 304.',
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-14T02:41:44Z'
                    WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185101'
                      AND "Rate" = 45.0000;
                    GET DIAGNOSTICS changed_rows = ROW_COUNT;

                    UPDATE tariffs
                    SET "Rate" = 7.4700,
                        "ElectricityFirstThreshold" = 1100.0000,
                        "ElectricitySecondThreshold" = 1700.0000,
                        "ElectricityFirstTierName" = 'До 1 100 кВт·ч',
                        "ElectricitySecondTierName" = 'От 1 100 до 1 700 кВт·ч',
                        "ElectricityThirdTierName" = 'Свыше 1 700 кВт·ч',
                        "ElectricityFirstRate" = 7.4700,
                        "ElectricitySecondRate" = 10.1700,
                        "ElectricityThirdRate" = 14.8800,
                        "Comment" = 'Краснодарский край, 01.01-30.09.2026: город без электроплит, приказ ДГРТ от 17.12.2025 № 18/2025-э.',
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-14T02:41:44Z'
                    WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185102'
                      AND "Rate" = 6.2000
                      AND "ElectricityFirstRate" = 6.2000
                      AND "ElectricitySecondRate" = 6.2000
                      AND "ElectricityThirdRate" = 6.2000;
                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    UPDATE tariffs
                    SET "Rate" = 128.6900,
                        "Comment" = 'Геленджик, 01.01-30.09.2026: ежемесячная плата с человека в МКД, Новороссийская зона ООО «Южный региональный оператор».',
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-14T02:41:44Z'
                    WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185105'
                      AND "Rate" = 300.0000;
                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    DELETE FROM irregular_payments
                    WHERE ("Id" = 'c865fd0a-ae14-4de6-83ef-b5d692327104' AND "Name" = 'Штраф за то')
                       OR ("Id" = 'c865fd0a-ae14-4de6-83ef-b5d692327105' AND "Name" = 'Штраф за это');
                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    IF changed_rows > 0 AND NOT EXISTS (
                        SELECT 1 FROM audit_events WHERE "Id" = '646d82e7-b8e0-4bdd-bd15-e7b4e1d5721b'
                    ) THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '646d82e7-b8e0-4bdd-bd15-e7b4e1d5721b', TIMESTAMPTZ '2026-07-14T02:41:44Z',
                            'dictionary.gelendzhik_tariff_defaults_applied', 'dictionary', 'update', 'tariff_catalog',
                            'gelendzhik-2026-01-09', 'Тарифы Геленджика',
                            'Преднаполнение тарифов обновлено по опубликованным ставкам на период с января по сентябрь 2026 года.',
                            '{"water":100.60,"electricity":[7.47,10.17,14.88],"wastePerPerson":128.69,"removedDemoPayments":2}'
                        );
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE tariffs
                SET "Rate" = 45.0000,
                    "Comment" = 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.'
                WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185101'
                  AND "Rate" = 100.6000;

                UPDATE tariffs
                SET "Rate" = 6.2000,
                    "ElectricityFirstThreshold" = 1.0000,
                    "ElectricitySecondThreshold" = 3.0000,
                    "ElectricityFirstTierName" = 'От 0 кВт',
                    "ElectricitySecondTierName" = 'От 1 кВт',
                    "ElectricityThirdTierName" = 'От 3 кВт',
                    "ElectricityFirstRate" = 6.2000,
                    "ElectricitySecondRate" = 6.2000,
                    "ElectricityThirdRate" = 6.2000,
                    "Comment" = 'Демонстрационные данные этапа 8. Перед реальным учетом проверьте и замените ставку.'
                WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185102'
                  AND "Rate" = 7.4700;

                UPDATE tariffs
                SET "Rate" = 300.0000,
                    "Comment" = 'Демонстрационные данные из формы ГСК. Перед реальным учетом проверьте и замените ставку.'
                WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185105'
                  AND "Rate" = 128.6900;

                INSERT INTO irregular_payments ("Id", "Name", "Amount", "IsActive", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ('c865fd0a-ae14-4de6-83ef-b5d692327104', 'Штраф за то', 500.00, TRUE, FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z'),
                    ('c865fd0a-ae14-4de6-83ef-b5d692327105', 'Штраф за это', 1000.00, TRUE, FALSE, TIMESTAMPTZ '2026-07-13T06:52:13Z', TIMESTAMPTZ '2026-07-13T06:52:13Z')
                ON CONFLICT ("Id") DO NOTHING;

                DELETE FROM audit_events
                WHERE "Id" = '646d82e7-b8e0-4bdd-bd15-e7b4e1d5721b';
                """);
        }
    }
}
