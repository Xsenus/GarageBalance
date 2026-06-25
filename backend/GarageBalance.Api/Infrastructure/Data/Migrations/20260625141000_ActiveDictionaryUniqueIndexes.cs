using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActiveDictionaryUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_Name_EffectiveFrom",
                table: "tariffs");

            migrationBuilder.DropIndex(
                name: "IX_supplier_groups_Name",
                table: "supplier_groups");

            migrationBuilder.DropIndex(
                name: "IX_income_types_Name",
                table: "income_types");

            migrationBuilder.DropIndex(
                name: "IX_expense_types_Name",
                table: "expense_types");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_Name_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "Name", "EffectiveFrom" },
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_groups_Name",
                table: "supplier_groups",
                column: "Name",
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Name",
                table: "income_types",
                column: "Name",
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Name",
                table: "expense_types",
                column: "Name",
                unique: true,
                filter: "\"IsArchived\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_Name_EffectiveFrom",
                table: "tariffs");

            migrationBuilder.DropIndex(
                name: "IX_supplier_groups_Name",
                table: "supplier_groups");

            migrationBuilder.DropIndex(
                name: "IX_income_types_Name",
                table: "income_types");

            migrationBuilder.DropIndex(
                name: "IX_expense_types_Name",
                table: "expense_types");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_Name_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "Name", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_groups_Name",
                table: "supplier_groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Name",
                table: "income_types",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Name",
                table: "expense_types",
                column: "Name",
                unique: true);
        }
    }
}
