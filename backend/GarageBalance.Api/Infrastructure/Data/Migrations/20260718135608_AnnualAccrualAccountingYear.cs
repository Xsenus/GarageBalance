using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AnnualAccrualAccountingYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountingYear",
                table: "accruals",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE accruals AS accrual
                SET "AccountingYear" = EXTRACT(YEAR FROM accrual."AccountingMonth")::integer
                FROM income_types AS income_type
                WHERE accrual."IncomeTypeId" = income_type."Id"
                  AND LOWER(BTRIM(COALESCE(income_type."Code", ''))) IN ('membership', 'target', 'outdoor_lighting');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingYear_IsCanceled",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingYear", "IsCanceled" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_accruals_AccountingYear_Range",
                table: "accruals",
                sql: "\"AccountingYear\" IS NULL OR (\"AccountingYear\" >= 1900 AND \"AccountingYear\" <= 9999)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingYear_IsCanceled",
                table: "accruals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_accruals_AccountingYear_Range",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "AccountingYear",
                table: "accruals");
        }
    }
}
