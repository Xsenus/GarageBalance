using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeDemoTariffCatalogRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    UPDATE charge_service_settings
                    SET "TariffId" = '8a92bf70-9339-4bbc-8e5d-a05cda185106',
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-13T16:03:38Z'
                    WHERE "Id" = 'f0d7ed2e-ec55-42b4-8a79-01b37c287106'
                      AND "Name" = 'Наружное освещение'
                      AND "TariffId" IS NULL
                      AND EXISTS (
                          SELECT 1
                          FROM tariffs
                          WHERE "Id" = '8a92bf70-9339-4bbc-8e5d-a05cda185106'
                            AND "Name" = 'Наружное освещение'
                      );

                    IF FOUND THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '0ba1e054-487e-4ff1-ae15-6ad3e17172aa', TIMESTAMPTZ '2026-07-13T16:03:38Z',
                            'dictionary.demo_tariff_catalog_normalized', 'dictionary', 'update', 'charge_service_setting',
                            'f0d7ed2e-ec55-42b4-8a79-01b37c287106', 'Наружное освещение',
                            'Настройка наружного освещения связана с единственным преднаполненным тарифом без создания дублирующих строк.',
                            '{"tariffId":"8a92bf70-9339-4bbc-8e5d-a05cda185106","reason":"normalize-demo-tariff-catalog"}'
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
                UPDATE charge_service_settings
                SET "TariffId" = NULL,
                    "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-13T06:52:13Z'
                WHERE "Id" = 'f0d7ed2e-ec55-42b4-8a79-01b37c287106'
                  AND "TariffId" = '8a92bf70-9339-4bbc-8e5d-a05cda185106';

                DELETE FROM audit_events
                WHERE "Id" = '0ba1e054-487e-4ff1-ae15-6ad3e17172aa';
                """);
        }
    }
}
