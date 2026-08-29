using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFinanceWorkingListSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            CreateTrigramIndex(migrationBuilder, "IX_accruals_Basis_trgm", "accruals", "\"Basis\"");
            CreateTrigramIndex(
                migrationBuilder,
                "IX_irregular_payments_Name_trgm",
                "irregular_payments",
                "\"Name\"");
            CreateTrigramIndex(migrationBuilder, "IX_fee_campaigns_Name_trgm", "fee_campaigns", "\"Name\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIndex(migrationBuilder, "IX_fee_campaigns_Name_trgm");
            DropIndex(migrationBuilder, "IX_irregular_payments_Name_trgm");
            DropIndex(migrationBuilder, "IX_accruals_Basis_trgm");
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
