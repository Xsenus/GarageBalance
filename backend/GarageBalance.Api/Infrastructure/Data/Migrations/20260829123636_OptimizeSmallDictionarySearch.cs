using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeSmallDictionarySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_measurement_units_Name_trgm" ON measurement_units USING gin (("Name") gin_trgm_ops);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_measurement_units_Name_trgm";""");
        }
    }
}
