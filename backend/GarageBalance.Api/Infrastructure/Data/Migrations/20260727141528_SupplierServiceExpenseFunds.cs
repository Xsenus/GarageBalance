using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierServiceExpenseFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseFundId",
                table: "supplier_accruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseFundId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseFundId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE charge_service_settings service
                SET "ExpenseFundId" = income_type."DestinationFundId"
                FROM income_types income_type
                WHERE service."IncomeTypeId" = income_type."Id"
                  AND service."ExpenseTypeId" IS NOT NULL
                  AND income_type."DestinationFundId" IS NOT NULL
                  AND service."ExpenseFundId" IS NULL;

                UPDATE supplier_accruals accrual
                SET "ExpenseFundId" = service."ExpenseFundId"
                FROM suppliers supplier
                INNER JOIN charge_service_settings service
                    ON service."Id" = supplier."ChargeServiceSettingId"
                WHERE accrual."SupplierId" = supplier."Id"
                  AND accrual."ExpenseFundId" IS NULL
                  AND service."ExpenseFundId" IS NOT NULL;

                UPDATE financial_operations operation
                SET "ExpenseFundId" = service."ExpenseFundId"
                FROM suppliers supplier
                INNER JOIN charge_service_settings service
                    ON service."Id" = supplier."ChargeServiceSettingId"
                WHERE operation."SupplierId" = supplier."Id"
                  AND operation."OperationKind" = 'expense'
                  AND operation."ExpenseFundId" IS NULL
                  AND service."ExpenseFundId" IS NOT NULL;

                INSERT INTO fund_operations (
                    "Id",
                    "FundId",
                    "SourceFinancialOperationId",
                    "OperationKind",
                    "Amount",
                    "BalanceBefore",
                    "BalanceAfter",
                    "Reason",
                    "IsCanceled",
                    "ActorUserId",
                    "CreatedAtUtc",
                    "UpdatedAtUtc")
                SELECT
                    md5(operation."Id"::text || ':expense-fund-disbursement')::uuid,
                    operation."ExpenseFundId",
                    operation."Id",
                    'withdraw',
                    operation."Amount",
                    0,
                    0,
                    'Выплата поставщику «' || supplier."Name" || '» по услуге «' || expense_type."Name" || '».',
                    operation."IsCanceled",
                    NULL,
                    operation."CreatedAtUtc",
                    operation."UpdatedAtUtc"
                FROM financial_operations operation
                INNER JOIN suppliers supplier ON supplier."Id" = operation."SupplierId"
                INNER JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE operation."OperationKind" = 'expense'
                  AND operation."SupplierId" IS NOT NULL
                  AND operation."ExpenseFundId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM fund_operations existing
                      WHERE existing."SourceFinancialOperationId" = operation."Id");

                WITH operation_balances AS (
                    SELECT
                        operation."Id",
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN operation."IsCanceled" THEN 0
                                    WHEN operation."SourceFinancialOperationId" IS NOT NULL
                                         AND operation."OperationKind" = 'deposit' THEN 0
                                    WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                                    ELSE -operation."Amount"
                                END)
                            OVER (
                                PARTITION BY operation."FundId"
                                ORDER BY operation."CreatedAtUtc", operation."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
                            0) AS balance_before,
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN operation."IsCanceled" THEN 0
                                    WHEN operation."SourceFinancialOperationId" IS NOT NULL
                                         AND operation."OperationKind" = 'deposit' THEN 0
                                    WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                                    ELSE -operation."Amount"
                                END)
                            OVER (
                                PARTITION BY operation."FundId"
                                ORDER BY operation."CreatedAtUtc", operation."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW),
                            0) AS balance_after
                    FROM fund_operations operation)
                UPDATE fund_operations operation
                SET "BalanceBefore" = balances.balance_before,
                    "BalanceAfter" = balances.balance_after
                FROM operation_balances balances
                WHERE operation."Id" = balances."Id";

                UPDATE funds fund
                SET "Balance" = COALESCE((
                        SELECT operation."BalanceAfter"
                        FROM fund_operations operation
                        WHERE operation."FundId" = fund."Id"
                        ORDER BY operation."CreatedAtUtc" DESC, operation."Id" DESC
                        LIMIT 1),
                    0),
                    "UpdatedAtUtc" = NOW();
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_charge_service_settings_ExpenseFundLink",
                table: "charge_service_settings",
                sql: "\"ExpenseFundId\" IS NULL OR \"ExpenseTypeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_ExpenseFundId",
                table: "supplier_accruals",
                column: "ExpenseFundId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_ExpenseFundId",
                table: "financial_operations",
                column: "ExpenseFundId");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_ExpenseFundId",
                table: "charge_service_settings",
                column: "ExpenseFundId");

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_funds_ExpenseFundId",
                table: "charge_service_settings",
                column: "ExpenseFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_financial_operations_funds_ExpenseFundId",
                table: "financial_operations",
                column: "ExpenseFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_accruals_funds_ExpenseFundId",
                table: "supplier_accruals",
                column: "ExpenseFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_charge_service_settings_ExpenseFundLink",
                table: "charge_service_settings");

            migrationBuilder.Sql(
                """
                DELETE FROM fund_operations
                WHERE "SourceFinancialOperationId" IS NOT NULL
                  AND "OperationKind" = 'withdraw';

                WITH operation_balances AS (
                    SELECT
                        operation."Id",
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN operation."IsCanceled" THEN 0
                                    WHEN operation."SourceFinancialOperationId" IS NOT NULL THEN 0
                                    WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                                    ELSE -operation."Amount"
                                END)
                            OVER (
                                PARTITION BY operation."FundId"
                                ORDER BY operation."CreatedAtUtc", operation."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING),
                            0) AS balance_before,
                        COALESCE(
                            SUM(
                                CASE
                                    WHEN operation."IsCanceled" THEN 0
                                    WHEN operation."SourceFinancialOperationId" IS NOT NULL THEN 0
                                    WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                                    ELSE -operation."Amount"
                                END)
                            OVER (
                                PARTITION BY operation."FundId"
                                ORDER BY operation."CreatedAtUtc", operation."Id"
                                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW),
                            0) AS balance_after
                    FROM fund_operations operation)
                UPDATE fund_operations operation
                SET "BalanceBefore" = balances.balance_before,
                    "BalanceAfter" = balances.balance_after
                FROM operation_balances balances
                WHERE operation."Id" = balances."Id";

                UPDATE funds fund
                SET "Balance" = COALESCE((
                        SELECT operation."BalanceAfter"
                        FROM fund_operations operation
                        WHERE operation."FundId" = fund."Id"
                        ORDER BY operation."CreatedAtUtc" DESC, operation."Id" DESC
                        LIMIT 1),
                    0),
                    "UpdatedAtUtc" = NOW();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_funds_ExpenseFundId",
                table: "charge_service_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_financial_operations_funds_ExpenseFundId",
                table: "financial_operations");

            migrationBuilder.DropForeignKey(
                name: "FK_supplier_accruals_funds_ExpenseFundId",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_supplier_accruals_ExpenseFundId",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_ExpenseFundId",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_ExpenseFundId",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "ExpenseFundId",
                table: "supplier_accruals");

            migrationBuilder.DropColumn(
                name: "ExpenseFundId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "ExpenseFundId",
                table: "charge_service_settings");
        }
    }
}
