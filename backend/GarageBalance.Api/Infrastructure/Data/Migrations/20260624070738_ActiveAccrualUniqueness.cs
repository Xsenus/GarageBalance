using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActiveAccrualUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~",
                table: "supplier_accruals",
                columns: new[] { "SupplierId", "ExpenseTypeId", "AccountingMonth", "Source", "DocumentNumber" },
                unique: true,
                filter: "\"IsCanceled\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~",
                table: "supplier_accruals",
                columns: new[] { "SupplierId", "ExpenseTypeId", "AccountingMonth", "Source", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true);
        }
    }
}
