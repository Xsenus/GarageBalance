using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActiveMeterReadingUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_meter_readings_GarageId_MeterKind_AccountingMonth",
                table: "meter_readings");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_GarageId_MeterKind_AccountingMonth",
                table: "meter_readings",
                columns: new[] { "GarageId", "MeterKind", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_meter_readings_GarageId_MeterKind_AccountingMonth",
                table: "meter_readings");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_GarageId_MeterKind_AccountingMonth",
                table: "meter_readings",
                columns: new[] { "GarageId", "MeterKind", "AccountingMonth" },
                unique: true);
        }
    }
}
