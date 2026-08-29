using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFinanceSummarySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            CreateTrigramIndex(
                migrationBuilder,
                "IX_financial_operations_CounterpartyName_trgm",
                "financial_operations",
                "\"CounterpartyName\"");
            CreateTrigramIndex(migrationBuilder, "IX_accruals_Comment_trgm", "accruals", "\"Comment\"");
            CreateTrigramIndex(migrationBuilder, "IX_meter_readings_Comment_trgm", "meter_readings", "\"Comment\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_supplier_accruals_Comment_trgm",
                "supplier_accruals",
                "\"Comment\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIndex(migrationBuilder, "IX_supplier_accruals_Comment_trgm");
            DropIndex(migrationBuilder, "IX_meter_readings_Comment_trgm");
            DropIndex(migrationBuilder, "IX_accruals_Comment_trgm");
            DropIndex(migrationBuilder, "IX_financial_operations_CounterpartyName_trgm");
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
