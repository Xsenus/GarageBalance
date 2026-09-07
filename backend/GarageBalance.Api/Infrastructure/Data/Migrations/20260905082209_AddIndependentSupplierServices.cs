using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndependentSupplierServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierServiceId",
                table: "suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supplier_services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_services", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_SupplierServiceId",
                table: "suppliers",
                column: "SupplierServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_services_Name",
                table: "supplier_services",
                column: "Name",
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_supplier_services_SupplierServiceId",
                table: "suppliers",
                column: "SupplierServiceId",
                principalTable: "supplier_services",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                INSERT INTO supplier_services ("Id", "Name", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT service."Id", service."Name", service."IsArchived", service."CreatedAtUtc", service."UpdatedAtUtc", gen_random_uuid()
                FROM charge_service_settings AS service
                WHERE EXISTS (SELECT 1 FROM suppliers AS supplier WHERE supplier."ChargeServiceSettingId" = service."Id");

                INSERT INTO supplier_services ("Id", "Name", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT gen_random_uuid(), supplier_group."Name", supplier_group."IsArchived",
                    MIN(supplier_group."CreatedAtUtc"), MAX(supplier_group."UpdatedAtUtc"), gen_random_uuid()
                FROM supplier_groups AS supplier_group
                WHERE EXISTS (SELECT 1 FROM suppliers AS supplier WHERE supplier."GroupId" = supplier_group."Id" AND supplier."ChargeServiceSettingId" IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM supplier_services AS service WHERE service."Name" = supplier_group."Name" AND service."IsArchived" = supplier_group."IsArchived")
                GROUP BY supplier_group."Name", supplier_group."IsArchived";

                UPDATE suppliers AS supplier
                SET "SupplierServiceId" = supplier."ChargeServiceSettingId"
                WHERE supplier."ChargeServiceSettingId" IS NOT NULL;

                UPDATE suppliers AS supplier
                SET "SupplierServiceId" = (
                    SELECT service."Id" FROM supplier_services AS service
                    WHERE service."Name" = supplier_group."Name" AND service."IsArchived" = supplier_group."IsArchived"
                    ORDER BY service."Id" LIMIT 1)
                FROM supplier_groups AS supplier_group
                WHERE supplier."GroupId" = supplier_group."Id" AND supplier."ChargeServiceSettingId" IS NULL;

                INSERT INTO audit_events ("Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityDisplayName", "Summary", "MetadataJson")
                SELECT 'f5f26672-2e12-4bb8-a4b8-cf8b8c996a3f', CURRENT_TIMESTAMP,
                    'dictionary.supplier_services_prepared', 'dictionary', 'create', 'supplier_service',
                    'Услуги поставщиков', 'Подготовлен независимый справочник наименований услуг поставщиков с сохранением исходных связей и финансовой истории.',
                    jsonb_build_object('serviceCount', (SELECT count(*) FROM supplier_services), 'supplierCount', count(*))::text
                FROM suppliers WHERE "SupplierServiceId" IS NOT NULL
                HAVING count(*) > 0
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_supplier_services_SupplierServiceId",
                table: "suppliers");

            migrationBuilder.DropTable(
                name: "supplier_services");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_SupplierServiceId",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "SupplierServiceId",
                table: "suppliers");
        }
    }
}
