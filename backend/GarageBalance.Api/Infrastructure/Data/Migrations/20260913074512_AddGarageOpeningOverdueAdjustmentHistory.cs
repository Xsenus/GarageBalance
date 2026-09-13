using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageOpeningOverdueAdjustmentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NewOverdueDebt",
                table: "opening_balance_adjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousOverdueDebt",
                table: "opening_balance_adjustments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewOverdueDebt",
                table: "opening_balance_adjustments");

            migrationBuilder.DropColumn(
                name: "PreviousOverdueDebt",
                table: "opening_balance_adjustments");
        }
    }
}
