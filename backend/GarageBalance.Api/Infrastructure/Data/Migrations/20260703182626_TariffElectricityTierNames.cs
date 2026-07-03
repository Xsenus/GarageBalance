using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TariffElectricityTierNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ElectricityFirstTierName",
                table: "tariffs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElectricitySecondTierName",
                table: "tariffs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElectricityThirdTierName",
                table: "tariffs",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElectricityFirstTierName",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricitySecondTierName",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ElectricityThirdTierName",
                table: "tariffs");
        }
    }
}
