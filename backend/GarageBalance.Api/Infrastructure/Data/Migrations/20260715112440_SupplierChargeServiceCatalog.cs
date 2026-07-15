using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierChargeServiceCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChargeServiceSettingId",
                table: "suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    linked_supplier_count integer;
                BEGIN
                    UPDATE suppliers AS supplier
                    SET "ChargeServiceSettingId" = matched_service."Id"
                    FROM supplier_groups AS supplier_group
                    CROSS JOIN LATERAL (
                        SELECT service."Id"
                        FROM charge_service_settings AS service
                        WHERE NOT service."IsArchived"
                          AND lower(btrim(service."Name")) = lower(btrim(supplier_group."Name"))
                        ORDER BY service."CreatedAtUtc", service."Id"
                        LIMIT 1
                    ) AS matched_service
                    WHERE supplier."GroupId" = supplier_group."Id"
                      AND supplier."ChargeServiceSettingId" IS NULL;

                    GET DIAGNOSTICS linked_supplier_count = ROW_COUNT;
                    IF linked_supplier_count > 0 THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '68ba5746-e568-4ee8-b47f-763ca316baf4', TIMESTAMPTZ '2026-07-15T11:24:40Z',
                            'dictionary.supplier_services_unified', 'dictionary', 'update', 'supplier',
                            'Услуги поставщиков',
                            'Существующие поставщики связаны с единым каталогом услуг раздела «Тарифы и сборы».',
                            jsonb_build_object('linkedSupplierCount', linked_supplier_count, 'reason', 'unify-supplier-service-catalog')::text
                        )
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_ChargeServiceSettingId",
                table: "suppliers",
                column: "ChargeServiceSettingId");

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_charge_service_settings_ChargeServiceSettingId",
                table: "suppliers",
                column: "ChargeServiceSettingId",
                principalTable: "charge_service_settings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM audit_events
                WHERE "Id" = '68ba5746-e568-4ee8-b47f-763ca316baf4';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_charge_service_settings_ChargeServiceSettingId",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_ChargeServiceSettingId",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "ChargeServiceSettingId",
                table: "suppliers");
        }
    }
}
