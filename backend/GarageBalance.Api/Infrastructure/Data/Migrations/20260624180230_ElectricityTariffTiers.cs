using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ElectricityTariffTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityFirstRate",
                table: "tariffs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityFirstThreshold",
                table: "tariffs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ElectricitySecondRate",
                table: "tariffs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ElectricitySecondThreshold",
                table: "tariffs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ElectricityThirdRate",
                table: "tariffs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElectricityFirstRate",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricityFirstThreshold",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricitySecondRate",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricitySecondThreshold",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricityThirdRate",
                table: "tariffs");
        }
    }
}
