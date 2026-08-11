using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkIrregularPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IrregularPaymentId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_IrregularPaymentId",
                table: "financial_operations",
                column: "IrregularPaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_operations_irregular_payments_IrregularPaymentId",
                table: "financial_operations",
                column: "IrregularPaymentId",
                principalTable: "irregular_payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE financial_operations AS operation
                SET "IrregularPaymentId" = matched."IrregularPaymentId"
                FROM (
                    SELECT allocation."FinancialOperationId",
                           (array_agg(DISTINCT accrual."IrregularPaymentId"))[1] AS "IrregularPaymentId"
                    FROM accrual_payment_allocations AS allocation
                    INNER JOIN accruals AS accrual ON accrual."Id" = allocation."AccrualId"
                    WHERE allocation."IsActive" = TRUE
                    GROUP BY allocation."FinancialOperationId"
                    HAVING count(DISTINCT accrual."IrregularPaymentId") = 1
                       AND count(*) FILTER (WHERE accrual."IrregularPaymentId" IS NULL) = 0
                ) AS matched
                WHERE operation."Id" = matched."FinancialOperationId"
                  AND operation."OperationKind" = 'income'
                  AND operation."IrregularPaymentId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_operations_irregular_payments_IrregularPaymentId",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_IrregularPaymentId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "IrregularPaymentId",
                table: "financial_operations");
        }
    }
}
