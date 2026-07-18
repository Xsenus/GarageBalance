using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkIncomeFundAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceFinancialOperationId",
                table: "fund_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    routed_active_total numeric(18,2);
                    available_total numeric(18,2);
                BEGIN
                    SELECT COALESCE(SUM(operation."Amount"), 0)
                    INTO routed_active_total
                    FROM financial_operations operation
                    INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
                    WHERE operation."OperationKind" = 'income'
                      AND operation."IsCanceled" = FALSE
                      AND income_type."DestinationFundId" IS NOT NULL;

                    SELECT
                        COALESCE(SUM(CASE
                            WHEN operation."OperationKind" = 'income' THEN operation."Amount"
                            WHEN operation."OperationKind" = 'expense' THEN -operation."Amount"
                            ELSE 0
                        END) FILTER (WHERE operation."IsCanceled" = FALSE), 0)
                        - COALESCE((SELECT SUM(fund."Balance") FROM funds fund), 0)
                    INTO available_total
                    FROM financial_operations operation;

                    IF routed_active_total > 0 AND routed_active_total > available_total THEN
                        RAISE EXCEPTION
                            'Historical routed income requires % but only % remains unallocated; reconcile existing fund distributions before applying LinkIncomeFundAssignments',
                            routed_active_total,
                            available_total;
                    END IF;
                END $$;

                INSERT INTO fund_operations (
                    "Id", "FundId", "OperationKind", "Amount", "BalanceBefore", "BalanceAfter",
                    "Reason", "ActorUserId", "CreatedAtUtc", "UpdatedAtUtc", "IsCanceled",
                    "IsCashToBankTransfer", "SourceFinancialOperationId")
                SELECT
                    md5(operation."Id"::text || ':income-fund-assignment')::uuid,
                    income_type."DestinationFundId",
                    'deposit',
                    operation."Amount",
                    0,
                    0,
                    'Автоматическое назначение поступления «' || income_type."Name" || '»',
                    NULL,
                    operation."CreatedAtUtc",
                    operation."UpdatedAtUtc",
                    operation."IsCanceled",
                    FALSE,
                    operation."Id"
                FROM financial_operations operation
                INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
                WHERE operation."OperationKind" = 'income'
                  AND income_type."DestinationFundId" IS NOT NULL;

                WITH recalculated AS (
                    SELECT
                        operation."Id",
                        COALESCE(SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END) OVER (
                            PARTITION BY operation."FundId"
                            ORDER BY operation."CreatedAtUtc", operation."Id"
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS balance_before,
                        SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END) OVER (
                            PARTITION BY operation."FundId"
                            ORDER BY operation."CreatedAtUtc", operation."Id"
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance_after
                    FROM fund_operations operation
                    WHERE operation."IsCanceled" = FALSE
                )
                UPDATE fund_operations operation
                SET "BalanceBefore" = recalculated.balance_before,
                    "BalanceAfter" = recalculated.balance_after
                FROM recalculated
                WHERE recalculated."Id" = operation."Id";

                UPDATE funds fund
                SET "Balance" = COALESCE((
                        SELECT SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END)
                        FROM fund_operations operation
                        WHERE operation."FundId" = fund."Id"
                          AND operation."IsCanceled" = FALSE), 0),
                    "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-18T18:35:43Z';

                INSERT INTO audit_events (
                    "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                    "EntityDisplayName", "Summary", "MetadataJson", "RelatedDocumentId")
                SELECT
                    md5(operation."Id"::text || ':income-fund-assignment-audit')::uuid,
                    TIMESTAMPTZ '2026-07-18T18:35:43Z',
                    'fund.income_assignment_created',
                    'funds',
                    'create',
                    'fund_operation',
                    assignment."Id"::text,
                    fund."Name",
                    'Для исторического поступления восстановлено автоматическое назначение в фонд «' || fund."Name" || '».',
                    jsonb_build_object(
                        'automatic', TRUE,
                        'fundId', fund."Id",
                        'sourceFinancialOperationId', operation."Id",
                        'migrationBackfill', TRUE)::text,
                    operation."Id"::text
                FROM fund_operations assignment
                INNER JOIN financial_operations operation ON operation."Id" = assignment."SourceFinancialOperationId"
                INNER JOIN funds fund ON fund."Id" = assignment."FundId"
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_fund_operations_SourceFinancialOperationId",
                table: "fund_operations",
                column: "SourceFinancialOperationId",
                unique: true,
                filter: "\"SourceFinancialOperationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_fund_operations_financial_operations_SourceFinancialOperati~",
                table: "fund_operations",
                column: "SourceFinancialOperationId",
                principalTable: "financial_operations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM audit_events audit
                USING fund_operations assignment
                WHERE assignment."SourceFinancialOperationId" IS NOT NULL
                  AND audit."Id" = md5(assignment."SourceFinancialOperationId"::text || ':income-fund-assignment-audit')::uuid;

                DELETE FROM fund_operations
                WHERE "SourceFinancialOperationId" IS NOT NULL;

                WITH recalculated AS (
                    SELECT
                        operation."Id",
                        COALESCE(SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END) OVER (
                            PARTITION BY operation."FundId"
                            ORDER BY operation."CreatedAtUtc", operation."Id"
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS balance_before,
                        SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END) OVER (
                            PARTITION BY operation."FundId"
                            ORDER BY operation."CreatedAtUtc", operation."Id"
                            ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS balance_after
                    FROM fund_operations operation
                    WHERE operation."IsCanceled" = FALSE
                )
                UPDATE fund_operations operation
                SET "BalanceBefore" = recalculated.balance_before,
                    "BalanceAfter" = recalculated.balance_after
                FROM recalculated
                WHERE recalculated."Id" = operation."Id";

                UPDATE funds fund
                SET "Balance" = COALESCE((
                        SELECT SUM(CASE
                            WHEN operation."OperationKind" = 'deposit' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END)
                        FROM fund_operations operation
                        WHERE operation."FundId" = fund."Id"
                          AND operation."IsCanceled" = FALSE), 0),
                    "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-18T18:35:43Z';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_fund_operations_financial_operations_SourceFinancialOperati~",
                table: "fund_operations");

            migrationBuilder.DropIndex(
                name: "IX_fund_operations_SourceFinancialOperationId",
                table: "fund_operations");

            migrationBuilder.DropColumn(
                name: "SourceFinancialOperationId",
                table: "fund_operations");
        }
    }
}
