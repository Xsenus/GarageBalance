using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceMeterKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeterKind",
                table: "charge_service_settings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE charge_service_settings AS service
                SET "MeterKind" = CASE
                    WHEN income_type."Code" = 'water' THEN 'water'
                    WHEN income_type."Code" = 'electricity' THEN 'electricity'
                    ELSE 'service_' || replace(service."Id"::text, '-', '')
                END
                FROM income_types AS income_type
                WHERE income_type."Id" = service."IncomeTypeId"
                  AND service."MeterKind" IS NULL;

                UPDATE charge_service_settings AS service
                SET "MeterKind" = 'service_' || replace(service."Id"::text, '-', '')
                WHERE service."MeterKind" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_MeterKind",
                table: "charge_service_settings",
                column: "MeterKind");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_MeterKind",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "MeterKind",
                table: "charge_service_settings");
        }
    }
}
