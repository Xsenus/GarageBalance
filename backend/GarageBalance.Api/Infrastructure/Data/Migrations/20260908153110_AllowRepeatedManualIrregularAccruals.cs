using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AllowRepeatedManualIrregularAccruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals",
                columns: new[] { "GarageId", "IrregularPaymentId", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"Source\" = 'regular' AND \"IrregularPaymentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals",
                columns: new[] { "GarageId", "IrregularPaymentId", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NOT NULL");
        }
    }
}
