using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageStartingOverdueDebt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StartingOverdueDebt",
                table: "garages",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Existing positive opening balances were treated as fully overdue before
            // this column existed. Preserve that behavior during the schema upgrade.
            migrationBuilder.Sql(
                "UPDATE \"garages\" SET \"StartingOverdueDebt\" = CASE WHEN \"StartingBalance\" > 0 THEN \"StartingBalance\" ELSE 0 END");

            migrationBuilder.AddCheckConstraint(
                name: "CK_garages_StartingOverdueDebt",
                table: "garages",
                sql: "\"StartingOverdueDebt\" IS NULL OR (CAST(\"StartingOverdueDebt\" AS NUMERIC) >= 0 AND CAST(\"StartingOverdueDebt\" AS NUMERIC) <= CASE WHEN CAST(\"StartingBalance\" AS NUMERIC) > 0 THEN CAST(\"StartingBalance\" AS NUMERIC) ELSE 0 END)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_garages_StartingOverdueDebt",
                table: "garages");

            migrationBuilder.DropColumn(
                name: "StartingOverdueDebt",
                table: "garages");
        }
    }
}
