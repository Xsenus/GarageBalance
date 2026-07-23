using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateExpensePaymentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceFinancialOperationId",
                table: "supplier_accruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpensePaymentType",
                table: "financial_operations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_SourceFinancialOperationId",
                table: "supplier_accruals",
                column: "SourceFinancialOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_ExpensePaymentType",
                table: "financial_operations",
                column: "ExpensePaymentType");

            migrationBuilder.AddCheckConstraint(
                name: "CK_financial_operations_ExpensePaymentType",
                table: "financial_operations",
                sql: "\"ExpensePaymentType\" IS NULL OR \"ExpensePaymentType\" IN ('with_receipt', 'without_receipt')");

            migrationBuilder.Sql(
                """
                UPDATE financial_operations AS operation
                SET "ExpensePaymentType" = CASE
                    WHEN LOWER(COALESCE(expense_type."Code", '')) IN (
                        'advance',
                        'advance_payment',
                        'advance_payments',
                        'cash_advance',
                        'no_receipt',
                        'without_receipt',
                        'no_check',
                        'without_check',
                        'cash_no_receipt')
                      OR LOWER(expense_type."Name") IN ('авансовые выплаты', 'выплата без чека')
                    THEN 'without_receipt'
                    ELSE 'with_receipt'
                END
                FROM expense_types AS expense_type
                WHERE operation."OperationKind" = 'expense'
                  AND operation."SupplierId" IS NOT NULL
                  AND operation."ExpenseTypeId" = expense_type."Id"
                  AND operation."ExpensePaymentType" IS NULL;

                WITH candidate_links AS (
                    SELECT operation."Id" AS operation_id,
                           accrual."Id" AS accrual_id,
                           COUNT(*) OVER (PARTITION BY operation."Id") AS operation_match_count,
                           COUNT(*) OVER (PARTITION BY accrual."Id") AS accrual_match_count
                    FROM financial_operations AS operation
                    INNER JOIN supplier_accruals AS accrual
                        ON accrual."SupplierId" = operation."SupplierId"
                       AND accrual."ExpenseTypeId" = operation."ExpenseTypeId"
                       AND accrual."AccountingMonth" = operation."AccountingMonth"
                       AND accrual."Amount" = operation."Amount"
                       AND accrual."Source" = 'manual'
                       AND accrual."DocumentNumber" IS NOT DISTINCT FROM operation."DocumentNumber"
                       AND ABS(EXTRACT(EPOCH FROM (accrual."CreatedAtUtc" - operation."CreatedAtUtc"))) <= 60
                    WHERE operation."ExpensePaymentType" = 'without_receipt'
                      AND operation."SupplierId" IS NOT NULL
                      AND accrual."SourceFinancialOperationId" IS NULL
                )
                UPDATE supplier_accruals AS accrual
                SET "SourceFinancialOperationId" = candidate.operation_id
                FROM candidate_links AS candidate
                WHERE accrual."Id" = candidate.accrual_id
                  AND candidate.operation_match_count = 1
                  AND candidate.accrual_match_count = 1;

                UPDATE supplier_accruals AS accrual
                SET "IsCanceled" = operation."IsCanceled",
                    "UpdatedAtUtc" = GREATEST(accrual."UpdatedAtUtc", operation."UpdatedAtUtc")
                FROM financial_operations AS operation
                WHERE accrual."SourceFinancialOperationId" = operation."Id"
                  AND accrual."IsCanceled" <> operation."IsCanceled";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_accruals_financial_operations_SourceFinancialOpera~",
                table: "supplier_accruals",
                column: "SourceFinancialOperationId",
                principalTable: "financial_operations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_accruals_financial_operations_SourceFinancialOpera~",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_supplier_accruals_SourceFinancialOperationId",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_ExpensePaymentType",
                table: "financial_operations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_financial_operations_ExpensePaymentType",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "SourceFinancialOperationId",
                table: "supplier_accruals");

            migrationBuilder.DropColumn(
                name: "ExpensePaymentType",
                table: "financial_operations");
        }
    }
}
