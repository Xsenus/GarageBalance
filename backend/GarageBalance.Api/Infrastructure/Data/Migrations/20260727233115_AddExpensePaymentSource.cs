using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpensePaymentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpensePaymentSource",
                table: "financial_operations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE financial_operations
                SET "ExpensePaymentSource" = CASE
                    WHEN "ExpensePaymentType" = 'without_receipt' THEN 'cash'
                    ELSE 'bank'
                END
                WHERE "OperationKind" = 'expense';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_ExpensePaymentSource",
                table: "financial_operations",
                column: "ExpensePaymentSource");

            migrationBuilder.AddCheckConstraint(
                name: "CK_financial_operations_ExpensePaymentSource",
                table: "financial_operations",
                sql: "\"ExpensePaymentSource\" IS NULL OR \"ExpensePaymentSource\" IN ('bank', 'cash')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_operations_ExpensePaymentSource",
                table: "financial_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_financial_operations_ExpensePaymentSource",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "ExpensePaymentSource",
                table: "financial_operations");
        }
    }
}
