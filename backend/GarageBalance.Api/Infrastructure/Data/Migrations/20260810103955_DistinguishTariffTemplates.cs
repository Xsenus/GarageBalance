using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DistinguishTariffTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                table: "tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE tariffs AS tariff
                SET "IsTemplate" = TRUE
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM charge_service_tariff_versions AS version
                    WHERE version."TariffId" = tariff."Id")
                  AND NOT EXISTS (
                    SELECT 1
                    FROM charge_service_settings AS setting
                    WHERE setting."TariffId" = tariff."Id")
                  AND tariff."Name" !~ ' — (обычный|по счетчику|по счетчику с порогами), [0-9]{2}[.][0-9]{2}[.][0-9]{4}, [0-9a-f]{8}$'
                  AND NOT (
                    tariff."Name" LIKE '% — тариф'
                    AND tariff."Comment" LIKE 'Создан вместе с услугой%');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_IsTemplate_IsArchived_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "IsTemplate", "IsArchived", "EffectiveFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_IsTemplate_IsArchived_EffectiveFrom",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                table: "tariffs");
        }
    }
}
