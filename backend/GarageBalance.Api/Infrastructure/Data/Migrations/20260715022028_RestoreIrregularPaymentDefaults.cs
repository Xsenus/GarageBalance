using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreIrregularPaymentDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    changed_rows integer := 0;
                BEGIN
                    WITH defaults("Id", "Name", "Amount") AS (
                        VALUES
                            ('c865fd0a-ae14-4de6-83ef-b5d692327101'::uuid, 'Вступительный взнос', 5000.00::numeric),
                            ('c865fd0a-ae14-4de6-83ef-b5d692327102'::uuid, 'Подключение канализации', 10000.00::numeric),
                            ('c865fd0a-ae14-4de6-83ef-b5d692327103'::uuid, 'Подключение к линии электросети', 15000.00::numeric)
                    ),
                    upserted AS (
                        INSERT INTO irregular_payments (
                            "Id", "Name", "Amount", "IsActive", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                        SELECT
                            defaults."Id",
                            defaults."Name",
                            defaults."Amount",
                            TRUE,
                            FALSE,
                            TIMESTAMPTZ '2026-07-15T02:20:28Z',
                            TIMESTAMPTZ '2026-07-15T02:20:28Z'
                        FROM defaults
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM irregular_payments existing
                            WHERE existing."Id" <> defaults."Id"
                              AND (
                                  LOWER(BTRIM(existing."Name")) = LOWER(BTRIM(defaults."Name"))
                                  OR (
                                      defaults."Id" = 'c865fd0a-ae14-4de6-83ef-b5d692327103'
                                      AND LOWER(BTRIM(existing."Name")) = LOWER('Подключение линии электросети')
                                  )
                              )
                        )
                        ON CONFLICT ("Id") DO UPDATE
                        SET "Name" = EXCLUDED."Name",
                            "IsActive" = TRUE,
                            "IsArchived" = FALSE,
                            "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                        RETURNING 1
                    )
                    SELECT COUNT(*) INTO changed_rows FROM upserted;

                    IF changed_rows > 0 AND NOT EXISTS (
                        SELECT 1 FROM audit_events WHERE "Id" = '38dffcf6-c534-4d97-99f7-aa61fd86de65'
                    ) THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '38dffcf6-c534-4d97-99f7-aa61fd86de65', TIMESTAMPTZ '2026-07-15T02:20:28Z',
                            'dictionary.irregular_payment_defaults_restored', 'dictionary', 'create', 'irregular_payment_catalog',
                            'customer-defaults', 'Нерегулярные платежи',
                            'В преднаполнение возвращены вступительный взнос и подключения к канализации и линии электросети.',
                            '{"irregularPaymentCount":3,"source":"customer request"}'
                        );
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM irregular_payments
                WHERE "Id" IN (
                    'c865fd0a-ae14-4de6-83ef-b5d692327101',
                    'c865fd0a-ae14-4de6-83ef-b5d692327102',
                    'c865fd0a-ae14-4de6-83ef-b5d692327103'
                )
                AND "CreatedAtUtc" = TIMESTAMPTZ '2026-07-15T02:20:28Z';

                DELETE FROM audit_events
                WHERE "Id" = '38dffcf6-c534-4d97-99f7-aa61fd86de65';
                """);
        }
    }
}
