using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GarageBalanceDbContext))]
    [Migration("20260805065345_ApplyAutomaticIncomeFundBalances")]
    public partial class ApplyAutomaticIncomeFundBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            RecalculateFundLedger(migrationBuilder, includeAutomaticIncomeDeposits: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RecalculateFundLedger(migrationBuilder, includeAutomaticIncomeDeposits: false);
        }

        private static void RecalculateFundLedger(
            MigrationBuilder migrationBuilder,
            bool includeAutomaticIncomeDeposits)
        {
            var ignoredAutomaticDepositCondition = includeAutomaticIncomeDeposits
                ? "FALSE"
                : "(operation.\"SourceFinancialOperationId\" IS NOT NULL AND operation.\"OperationKind\" = 'deposit')";
            migrationBuilder.Sql($$"""
                WITH RECURSIVE ordered_operations AS (
                    SELECT
                        operation."Id",
                        operation."FundId",
                        operation."OperationKind",
                        operation."Amount",
                        operation."IsCanceled",
                        operation."SourceFinancialOperationId",
                        operation."BalanceBefore" AS opening_balance,
                        ROW_NUMBER() OVER (
                            PARTITION BY operation."FundId"
                            ORDER BY operation."CreatedAtUtc", operation."Id") AS row_number
                    FROM fund_operations AS operation
                ),
                recalculated AS (
                    SELECT
                        operation."Id",
                        operation."FundId",
                        operation.row_number,
                        operation.opening_balance AS balance_before,
                        CAST(CASE
                            WHEN operation."IsCanceled" OR {{ignoredAutomaticDepositCondition}}
                                THEN operation.opening_balance
                            WHEN operation."OperationKind" = 'deposit'
                                THEN operation.opening_balance + operation."Amount"
                            ELSE operation.opening_balance - operation."Amount"
                        END AS numeric(18, 2)) AS balance_after
                    FROM ordered_operations AS operation
                    WHERE operation.row_number = 1

                    UNION ALL

                    SELECT
                        operation."Id",
                        operation."FundId",
                        operation.row_number,
                        previous.balance_after AS balance_before,
                        CAST(CASE
                            WHEN operation."IsCanceled" OR {{ignoredAutomaticDepositCondition}}
                                THEN previous.balance_after
                            WHEN operation."OperationKind" = 'deposit'
                                THEN previous.balance_after + operation."Amount"
                            ELSE previous.balance_after - operation."Amount"
                        END AS numeric(18, 2)) AS balance_after
                    FROM recalculated AS previous
                    INNER JOIN ordered_operations AS operation
                        ON operation."FundId" = previous."FundId"
                        AND operation.row_number = previous.row_number + 1
                )
                UPDATE fund_operations AS operation
                SET
                    "BalanceBefore" = recalculated.balance_before,
                    "BalanceAfter" = recalculated.balance_after
                FROM recalculated
                WHERE operation."Id" = recalculated."Id";

                UPDATE funds AS fund
                SET "Balance" = (
                    SELECT operation."BalanceAfter" AS balance_after
                    FROM fund_operations AS operation
                    WHERE operation."FundId" = fund."Id"
                    ORDER BY operation."CreatedAtUtc" DESC, operation."Id" DESC
                    LIMIT 1
                )
                WHERE EXISTS (
                    SELECT 1
                    FROM fund_operations AS operation
                    WHERE operation."FundId" = fund."Id");
                """);
        }
    }
}
