using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSupplierOpeningBalanceConsistency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_suppliers_StartingDebt",
                table: "suppliers");

            migrationBuilder.Sql(
                "UPDATE \"suppliers\" SET \"StartingDebt\" = CASE WHEN \"StartingBalance\" > 0 THEN \"StartingBalance\" ELSE 0 END WHERE \"StartingDebt\" IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_suppliers_StartingDebt",
                table: "suppliers",
                sql: "\"StartingDebt\" IS NULL OR (CAST(\"StartingDebt\" AS NUMERIC) = CASE WHEN CAST(\"StartingBalance\" AS NUMERIC) > 0 THEN CAST(\"StartingBalance\" AS NUMERIC) ELSE 0 END)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_suppliers_StartingDebt",
                table: "suppliers");

            migrationBuilder.AddCheckConstraint(
                name: "CK_suppliers_StartingDebt",
                table: "suppliers",
                sql: "\"StartingDebt\" IS NULL OR (CAST(\"StartingDebt\" AS NUMERIC) >= 0 AND CAST(\"StartingDebt\" AS NUMERIC) <= CASE WHEN CAST(\"StartingBalance\" AS NUMERIC) > 0 THEN CAST(\"StartingBalance\" AS NUMERIC) ELSE 0 END)");
        }
    }
}
