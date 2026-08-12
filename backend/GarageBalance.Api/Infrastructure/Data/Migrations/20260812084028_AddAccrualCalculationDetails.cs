using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccrualCalculationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationDetailsJson",
                table: "accruals",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalculationMeterKind",
                table: "accruals",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresMeterReading",
                table: "accruals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_AccountingMonth_RequiresMeterReading",
                table: "accruals",
                columns: new[] { "GarageId", "AccountingMonth", "RequiresMeterReading" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_AccountingMonth_RequiresMeterReading",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "CalculationDetailsJson",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "CalculationMeterKind",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "RequiresMeterReading",
                table: "accruals");
        }
    }
}
