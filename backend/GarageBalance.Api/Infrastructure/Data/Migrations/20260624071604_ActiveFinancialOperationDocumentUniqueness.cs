using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActiveFinancialOperationDocumentUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_DocumentNu~",
                table: "financial_operations");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_DocumentNu~",
                table: "financial_operations",
                columns: new[] { "OperationKind", "OperationDate", "DocumentNumber" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"DocumentNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_DocumentNu~",
                table: "financial_operations");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_DocumentNu~",
                table: "financial_operations",
                columns: new[] { "OperationKind", "OperationDate", "DocumentNumber" });
        }
    }
}
