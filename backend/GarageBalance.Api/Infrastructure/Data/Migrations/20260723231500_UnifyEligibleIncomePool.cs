using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260723231500_UnifyEligibleIncomePool")]
public sealed class UnifyEligibleIncomePool : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO funds (
                "Id", "Name", "NormalizedName", "Balance", "SortOrder",
                "AllowOperations", "IsSystem", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT *
            FROM (VALUES
                ('718b671d-9133-4ffd-a130-3f2f03a8d150'::uuid, 'Членские взносы', 'ЧЛЕНСКИЕ ВЗНОСЫ', 0::numeric, 50, TRUE, TRUE,
                 TIMESTAMPTZ '2026-07-23T16:15:00Z', TIMESTAMPTZ '2026-07-23T16:15:00Z'),
                ('edc66ae5-29a3-4dbd-921e-4a1958612760'::uuid, 'Целевые взносы', 'ЦЕЛЕВЫЕ ВЗНОСЫ', 0::numeric, 60, TRUE, TRUE,
                 TIMESTAMPTZ '2026-07-23T16:15:00Z', TIMESTAMPTZ '2026-07-23T16:15:00Z')
            ) AS defaults(
                "Id", "Name", "NormalizedName", "Balance", "SortOrder",
                "AllowOperations", "IsSystem", "CreatedAtUtc", "UpdatedAtUtc")
            WHERE NOT EXISTS (
                SELECT 1
                FROM funds existing
                WHERE existing."NormalizedName" = defaults."NormalizedName");

            UPDATE funds
            SET "AllowOperations" = TRUE,
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
            WHERE "NormalizedName" IN ('ЧЛЕНСКИЕ ВЗНОСЫ', 'ЦЕЛЕВЫЕ ВЗНОСЫ', 'ПРОЧЕЕ')
              AND "AllowOperations" = FALSE;

            UPDATE income_types income_type
            SET "DestinationFundId" = fund."Id",
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
            FROM funds fund
            WHERE (
                    (LOWER(BTRIM(income_type."Code")) = 'membership' AND fund."NormalizedName" = 'ЧЛЕНСКИЕ ВЗНОСЫ')
                 OR (LOWER(BTRIM(income_type."Code")) = 'target' AND fund."NormalizedName" = 'ЦЕЛЕВЫЕ ВЗНОСЫ'))
              AND income_type."DestinationFundId" IS DISTINCT FROM fund."Id";

            INSERT INTO fund_operations (
                "Id", "FundId", "OperationKind", "Amount", "BalanceBefore", "BalanceAfter",
                "Reason", "ActorUserId", "CreatedAtUtc", "UpdatedAtUtc", "IsCanceled",
                "SourceFinancialOperationId")
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
                operation."Id"
            FROM financial_operations operation
            INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
            WHERE operation."OperationKind" = 'income'
              AND LOWER(BTRIM(income_type."Code")) IN ('membership', 'target')
              AND income_type."DestinationFundId" IS NOT NULL
            ON CONFLICT ("SourceFinancialOperationId") WHERE "SourceFinancialOperationId" IS NOT NULL
            DO NOTHING;

            INSERT INTO audit_events (
                "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                "EntityDisplayName", "Summary", "MetadataJson", "RelatedDocumentId")
            SELECT
                md5(operation."Id"::text || ':unified-income-pool-audit')::uuid,
                TIMESTAMPTZ '2026-07-23T16:15:00Z',
                'fund.income_assignment_created',
                'funds',
                'create',
                'fund_operation',
                assignment."Id"::text,
                fund."Name",
                'Историческое поступление включено в общий нераспределенный пул.',
                jsonb_build_object(
                    'automatic', TRUE,
                    'fundId', fund."Id",
                    'sourceFinancialOperationId', operation."Id",
                    'unifiedPoolBackfill', TRUE)::text,
                operation."Id"::text
            FROM financial_operations operation
            INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
            INNER JOIN fund_operations assignment ON assignment."SourceFinancialOperationId" = operation."Id"
            INNER JOIN funds fund ON fund."Id" = assignment."FundId"
            WHERE LOWER(BTRIM(income_type."Code")) IN ('membership', 'target')
            ON CONFLICT ("Id") DO NOTHING;

            WITH recalculated AS (
                SELECT
                    operation."Id",
                    COALESCE(SUM(CASE
                        WHEN operation."IsCanceled" = FALSE
                         AND operation."SourceFinancialOperationId" IS NULL
                        THEN CASE WHEN operation."OperationKind" = 'deposit'
                            THEN operation."Amount" ELSE -operation."Amount" END
                        ELSE 0
                    END) OVER (
                        PARTITION BY operation."FundId"
                        ORDER BY operation."CreatedAtUtc", operation."Id"
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS balance_before,
                    CASE
                        WHEN operation."IsCanceled" = FALSE
                         AND operation."SourceFinancialOperationId" IS NULL
                        THEN CASE WHEN operation."OperationKind" = 'deposit'
                            THEN operation."Amount" ELSE -operation."Amount" END
                        ELSE 0
                    END AS balance_change
                FROM fund_operations operation
            )
            UPDATE fund_operations operation
            SET "BalanceBefore" = recalculated.balance_before,
                "BalanceAfter" = recalculated.balance_before + recalculated.balance_change
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
                      AND operation."IsCanceled" = FALSE
                      AND operation."SourceFinancialOperationId" IS NULL), 0),
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM audit_events
            WHERE "Id" IN (
                SELECT md5(operation."Id"::text || ':unified-income-pool-audit')::uuid
                FROM financial_operations operation);

            DELETE FROM fund_operations assignment
            USING financial_operations operation, income_types income_type
            WHERE assignment."SourceFinancialOperationId" = operation."Id"
              AND operation."IncomeTypeId" = income_type."Id"
              AND LOWER(BTRIM(income_type."Code")) IN ('membership', 'target');

            UPDATE income_types
            SET "DestinationFundId" = NULL,
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
            WHERE LOWER(BTRIM("Code")) IN ('membership', 'target');

            UPDATE funds
            SET "AllowOperations" = CASE
                    WHEN "NormalizedName" = 'ЦЕЛЕВЫЕ ВЗНОСЫ' THEN TRUE
                    ELSE FALSE
                END,
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
            WHERE "NormalizedName" IN ('ЧЛЕНСКИЕ ВЗНОСЫ', 'ЦЕЛЕВЫЕ ВЗНОСЫ', 'ПРОЧЕЕ');

            WITH recalculated AS (
                SELECT
                    operation."Id",
                    COALESCE(SUM(CASE
                        WHEN operation."IsCanceled" = FALSE
                        THEN CASE WHEN operation."OperationKind" = 'deposit'
                            THEN operation."Amount" ELSE -operation."Amount" END
                        ELSE 0
                    END) OVER (
                        PARTITION BY operation."FundId"
                        ORDER BY operation."CreatedAtUtc", operation."Id"
                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS balance_before,
                    CASE
                        WHEN operation."IsCanceled" = FALSE
                        THEN CASE WHEN operation."OperationKind" = 'deposit'
                            THEN operation."Amount" ELSE -operation."Amount" END
                        ELSE 0
                    END AS balance_change
                FROM fund_operations operation
            )
            UPDATE fund_operations operation
            SET "BalanceBefore" = recalculated.balance_before,
                "BalanceAfter" = recalculated.balance_before + recalculated.balance_change
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
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z';
            """);
    }
}
