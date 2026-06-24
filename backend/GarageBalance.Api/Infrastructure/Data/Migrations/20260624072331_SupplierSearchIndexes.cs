using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_suppliers_ContactPerson",
                table: "suppliers",
                column: "ContactPerson");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Inn",
                table: "suppliers",
                column: "Inn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_suppliers_ContactPerson",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_Inn",
                table: "suppliers");
        }
    }
}
