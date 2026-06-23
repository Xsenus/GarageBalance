using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GarageBalanceDbContext))]
    [Migration("20260623131000_ActiveGarageNumberIndex")]
    public partial class ActiveGarageNumberIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_garages_Number",
                table: "garages");

            migrationBuilder.CreateIndex(
                name: "IX_garages_Number",
                table: "garages",
                column: "Number",
                unique: true,
                filter: "\"IsArchived\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_garages_Number",
                table: "garages");

            migrationBuilder.CreateIndex(
                name: "IX_garages_Number",
                table: "garages",
                column: "Number",
                unique: true);
        }
    }
}
