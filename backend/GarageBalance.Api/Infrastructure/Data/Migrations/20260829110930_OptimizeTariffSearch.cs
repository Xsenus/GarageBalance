using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeTariffSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_tariffs_Name_trgm"
                ON tariffs USING gin (("Name") gin_trgm_ops);
                """);
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_tariffs_CalculationBase_trgm"
                ON tariffs USING gin (("CalculationBase") gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_tariffs_CalculationBase_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_tariffs_Name_trgm";""");
        }
    }
}
