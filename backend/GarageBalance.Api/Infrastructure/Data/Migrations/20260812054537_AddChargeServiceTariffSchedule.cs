using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeServiceTariffSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveTo",
                table: "charge_service_tariff_versions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "charge_service_tariff_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                WITH ordered_versions AS (
                    SELECT "ChargeServiceSettingId", "EffectiveFrom",
                           LEAD("EffectiveFrom") OVER (
                               PARTITION BY "ChargeServiceSettingId"
                               ORDER BY "EffectiveFrom") AS next_effective_from
                    FROM charge_service_tariff_versions
                )
                UPDATE charge_service_tariff_versions AS target
                SET "EffectiveTo" = ordered_versions.next_effective_from - 1
                FROM ordered_versions
                WHERE target."ChargeServiceSettingId" = ordered_versions."ChargeServiceSettingId"
                  AND target."EffectiveFrom" = ordered_versions."EffectiveFrom"
                  AND ordered_versions.next_effective_from IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_tariff_versions_ChargeServiceSettingId_Effec~",
                table: "charge_service_tariff_versions",
                columns: new[] { "ChargeServiceSettingId", "EffectiveFrom", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_tariff_versions_IsArchived",
                table: "charge_service_tariff_versions",
                column: "IsArchived");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_charge_service_tariff_versions_ChargeServiceSettingId_Effec~",
                table: "charge_service_tariff_versions");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_tariff_versions_IsArchived",
                table: "charge_service_tariff_versions");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "charge_service_tariff_versions");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "charge_service_tariff_versions");
        }
    }
}
