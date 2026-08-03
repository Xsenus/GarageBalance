using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeReportSearchExpressions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            CreateTrigramIndex(migrationBuilder, "IX_funds_Name_trgm", "funds", "\"Name\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_fund_operations_OperationKind_trgm",
                "fund_operations",
                "\"OperationKind\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_fund_operations_Reason_trgm",
                "fund_operations",
                "\"Reason\"");
            CreateTrigramIndex(migrationBuilder, "IX_expense_types_Name_trgm", "expense_types", "\"Name\"");
            CreateTrigramIndex(migrationBuilder, "IX_income_types_Name_trgm", "income_types", "\"Name\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_supplier_accruals_DocumentNumber_trgm",
                "supplier_accruals",
                "\"DocumentNumber\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_financial_operations_DocumentNumber_trgm",
                "financial_operations",
                "\"DocumentNumber\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_financial_operations_Comment_trgm",
                "financial_operations",
                "\"Comment\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_cash_bank_transfers_Comment_trgm",
                "cash_bank_transfers",
                "\"Comment\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIndex(migrationBuilder, "IX_cash_bank_transfers_Comment_trgm");
            DropIndex(migrationBuilder, "IX_financial_operations_Comment_trgm");
            DropIndex(migrationBuilder, "IX_financial_operations_DocumentNumber_trgm");
            DropIndex(migrationBuilder, "IX_supplier_accruals_DocumentNumber_trgm");
            DropIndex(migrationBuilder, "IX_income_types_Name_trgm");
            DropIndex(migrationBuilder, "IX_expense_types_Name_trgm");
            DropIndex(migrationBuilder, "IX_fund_operations_Reason_trgm");
            DropIndex(migrationBuilder, "IX_fund_operations_OperationKind_trgm");
            DropIndex(migrationBuilder, "IX_funds_Name_trgm");
        }

        private static void CreateTrigramIndex(
            MigrationBuilder migrationBuilder,
            string name,
            string table,
            string expression)
        {
            migrationBuilder.Sql(
                $"CREATE INDEX \"{name}\" ON {table} USING gin (({expression}) gin_trgm_ops);");
        }

        private static void DropIndex(MigrationBuilder migrationBuilder, string name) =>
            migrationBuilder.Sql($"DROP INDEX IF EXISTS \"{name}\";");
    }
}
