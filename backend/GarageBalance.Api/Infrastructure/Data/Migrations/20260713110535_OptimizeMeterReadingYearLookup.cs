using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeMeterReadingYearLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_MeterKind_AccountingMonth_GarageId",
                table: "meter_readings",
                columns: new[] { "MeterKind", "AccountingMonth", "GarageId" },
                filter: "\"IsCanceled\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_meter_readings_MeterKind_AccountingMonth_GarageId",
                table: "meter_readings");
        }
    }
}
