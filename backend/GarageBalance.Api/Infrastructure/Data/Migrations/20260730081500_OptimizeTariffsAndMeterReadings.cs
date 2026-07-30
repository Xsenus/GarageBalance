using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeTariffsAndMeterReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_tariffs_CalculationBase_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "CalculationBase", "EffectiveFrom" },
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_IsRegular_IsMetered_TariffId",
                table: "charge_service_settings",
                columns: new[] { "IsRegular", "IsMetered", "TariffId" },
                filter: "\"IsArchived\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_CalculationBase_EffectiveFrom",
                table: "tariffs");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_IsRegular_IsMetered_TariffId",
                table: "charge_service_settings");
        }
    }
}
