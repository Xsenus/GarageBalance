using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RouteIrregularAccrualsToOtherPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.AddColumn<Guid>(
                name: "IrregularPaymentId",
                table: "accruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals",
                columns: new[] { "GarageId", "IrregularPaymentId", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_IrregularPaymentId",
                table: "accruals",
                column: "IrregularPaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_accruals_irregular_payments_IrregularPaymentId",
                table: "accruals",
                column: "IrregularPaymentId",
                principalTable: "irregular_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accruals_irregular_payments_IrregularPaymentId",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IrregularPaymentId_AccountingMonth",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_IrregularPaymentId",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "IrregularPaymentId",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false");
        }
    }
}
