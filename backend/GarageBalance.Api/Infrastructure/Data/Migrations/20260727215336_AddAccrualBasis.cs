using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccrualBasis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.AddColumn<string>(
                name: "Basis",
                table: "accruals",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE accruals AS accrual
                SET "Basis" = payment."Name"
                FROM irregular_payments AS payment
                WHERE accrual."IrregularPaymentId" = payment."Id"
                  AND accrual."Basis" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL AND \"FeeCampaignId\" IS NULL AND \"Basis\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "Basis",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL AND \"FeeCampaignId\" IS NULL");
        }
    }
}
