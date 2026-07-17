using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DistinguishCashToBankFundTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCashToBankTransfer",
                table: "fund_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "fund_operations"
                SET "IsCashToBankTransfer" = TRUE
                WHERE "OperationKind" = 'deposit'
                  AND (
                    "Reason" LIKE 'Сдача кассы в банк%'
                    OR "Reason" LIKE 'Сдача наличных в банк%'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCashToBankTransfer",
                table: "fund_operations");
        }
    }
}
