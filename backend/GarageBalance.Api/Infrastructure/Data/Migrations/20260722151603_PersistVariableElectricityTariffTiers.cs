using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable


namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistVariableElectricityTariffTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ElectricityTiersJson",
                table: "tariffs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElectricityTiersJson",
                table: "tariffs");
        }
    }
}
