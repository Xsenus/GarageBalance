using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260729143835_LinkAllIncomeTypesToFunds")]
public sealed class LinkAllIncomeTypesToFunds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO funds (
                "Id", "Name", "NormalizedName", "Balance", "SortOrder",
                "AllowOperations", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT defaults.*
            FROM (VALUES
                ('16c1e6d9-52f8-4c57-9b4f-1cbd819d3981'::uuid, 'Электроэнергия', 'ЭЛЕКТРОЭНЕРГИЯ', 0::numeric, 10, TRUE, TRUE, FALSE,
                 TIMESTAMPTZ '2026-07-29T14:38:35Z', TIMESTAMPTZ '2026-07-29T14:38:35Z'),
                ('25e7390f-50ca-4d6a-871c-acde1563bffc'::uuid, 'Водоснабжение', 'ВОДОСНАБЖЕНИЕ', 0::numeric, 20, TRUE, TRUE, FALSE,
                 TIMESTAMPTZ '2026-07-29T14:38:35Z', TIMESTAMPTZ '2026-07-29T14:38:35Z'),
                ('3acaa67a-dcb3-4027-a199-0a27dbe426c1'::uuid, 'Вывоз мусора', 'ВЫВОЗ МУСОРА', 0::numeric, 30, TRUE, TRUE, FALSE,
                 TIMESTAMPTZ '2026-07-29T14:38:35Z', TIMESTAMPTZ '2026-07-29T14:38:35Z'),
                ('4ced0dd4-1635-44ea-82f9-85a6cf6217af'::uuid, 'Наружное освещение', 'НАРУЖНОЕ ОСВЕЩЕНИЕ', 0::numeric, 40, TRUE, TRUE, FALSE,
                 TIMESTAMPTZ '2026-07-29T14:38:35Z', TIMESTAMPTZ '2026-07-29T14:38:35Z')
            ) AS defaults(
                "Id", "Name", "NormalizedName", "Balance", "SortOrder",
                "AllowOperations", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            WHERE NOT EXISTS (
                SELECT 1
                FROM funds existing
                WHERE existing."NormalizedName" = defaults."NormalizedName"
                   OR (existing."IsSystem" = TRUE AND existing."SortOrder" = defaults."SortOrder"));

            WITH destination AS (
                SELECT
                    income_type."Id" AS income_type_id,
                    income_type."Name" AS income_type_name,
                    COALESCE(exact_fund."Id", other_fund."Id") AS fund_id
                FROM income_types income_type
                LEFT JOIN funds exact_fund
                  ON exact_fund."IsArchived" = FALSE
                 AND exact_fund."AllowOperations" = TRUE
                 AND exact_fund."NormalizedName" = CASE LOWER(BTRIM(COALESCE(income_type."Code", '')))
                     WHEN 'water' THEN 'ВОДОСНАБЖЕНИЕ'
                     WHEN 'trash' THEN 'ВЫВОЗ МУСОРА'
                     WHEN 'electricity' THEN 'ЭЛЕКТРОЭНЕРГИЯ'
                     WHEN 'outdoor_lighting' THEN 'НАРУЖНОЕ ОСВЕЩЕНИЕ'
                     ELSE NULL
                 END
                LEFT JOIN funds other_fund
                  ON other_fund."IsArchived" = FALSE
                 AND other_fund."AllowOperations" = TRUE
                 AND other_fund."NormalizedName" = 'ПРОЧЕЕ'
                WHERE income_type."DestinationFundId" IS NULL
                  AND income_type."IsArchived" = FALSE
            ),
            updated AS (
                UPDATE income_types income_type
                SET "DestinationFundId" = destination.fund_id,
                    "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-29T14:38:35Z'
                FROM destination
                WHERE destination.income_type_id = income_type."Id"
                  AND destination.fund_id IS NOT NULL
                RETURNING income_type."Id", income_type."Name", income_type."DestinationFundId")
            INSERT INTO audit_events (
                "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                "EntityDisplayName", "Summary", "MetadataJson", "RelatedDocumentId")
            SELECT
                md5(updated."Id"::text || ':income-type-fund-autolink')::uuid,
                TIMESTAMPTZ '2026-07-29T14:38:35Z',
                'dictionary.income_type_fund_autolinked',
                'dictionaries',
                'update',
                'income_type',
                updated."Id"::text,
                updated."Name",
                'Вид поступления автоматически связан с действующим фондом.',
                jsonb_build_object(
                    'automatic', TRUE,
                    'destinationFundId', updated."DestinationFundId",
                    'allIncomeTypeFundMapping', TRUE)::text,
                NULL
            FROM updated
            ON CONFLICT ("Id") DO NOTHING;

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
              AND income_type."DestinationFundId" IS NOT NULL
              AND EXISTS (
                  SELECT 1
                  FROM audit_events mapping
                  WHERE mapping."EntityType" = 'income_type'
                    AND mapping."EntityId" = income_type."Id"::text
                    AND COALESCE((mapping."MetadataJson"::jsonb ->> 'allIncomeTypeFundMapping')::boolean, FALSE))
              AND NOT EXISTS (
                  SELECT 1
                  FROM fund_operations existing
                  WHERE existing."SourceFinancialOperationId" = operation."Id")
            ON CONFLICT ("SourceFinancialOperationId") WHERE "SourceFinancialOperationId" IS NOT NULL
            DO NOTHING;

            INSERT INTO audit_events (
                "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                "EntityDisplayName", "Summary", "MetadataJson", "RelatedDocumentId")
            SELECT
                md5(operation."Id"::text || ':all-income-funds-audit')::uuid,
                TIMESTAMPTZ '2026-07-29T14:38:35Z',
                'fund.income_assignment_created',
                'funds',
                'create',
                'fund_operation',
                assignment."Id"::text,
                fund."Name",
                'Историческое поступление автоматически связано с фондом.',
                jsonb_build_object(
                    'automatic', TRUE,
                    'fundId', fund."Id",
                    'sourceFinancialOperationId', operation."Id",
                    'allIncomeFundsBackfill', TRUE)::text,
                operation."Id"::text
            FROM financial_operations operation
            INNER JOIN fund_operations assignment ON assignment."SourceFinancialOperationId" = operation."Id"
            INNER JOIN funds fund ON fund."Id" = assignment."FundId"
            WHERE operation."OperationKind" = 'income'
              AND EXISTS (
                  SELECT 1
                  FROM audit_events mapping
                  WHERE mapping."EntityType" = 'income_type'
                    AND mapping."EntityId" = operation."IncomeTypeId"::text
                    AND COALESCE((mapping."MetadataJson"::jsonb ->> 'allIncomeTypeFundMapping')::boolean, FALSE))
              AND NOT EXISTS (
                  SELECT 1
                  FROM audit_events existing
                  WHERE existing."Id" = md5(operation."Id"::text || ':all-income-funds-audit')::uuid)
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
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM fund_operations assignment
            USING financial_operations operation, audit_events mapping
            WHERE assignment."SourceFinancialOperationId" = operation."Id"
              AND mapping."EntityType" = 'income_type'
              AND mapping."EntityId" = operation."IncomeTypeId"::text
              AND COALESCE((mapping."MetadataJson"::jsonb ->> 'allIncomeTypeFundMapping')::boolean, FALSE);

            UPDATE income_types income_type
            SET "DestinationFundId" = NULL,
                "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-29T14:38:35Z'
            FROM audit_events mapping
            WHERE mapping."EntityType" = 'income_type'
              AND mapping."EntityId" = income_type."Id"::text
              AND COALESCE((mapping."MetadataJson"::jsonb ->> 'allIncomeTypeFundMapping')::boolean, FALSE);

            DELETE FROM audit_events
            WHERE COALESCE(("MetadataJson"::jsonb ->> 'allIncomeFundsBackfill')::boolean, FALSE)
               OR COALESCE(("MetadataJson"::jsonb ->> 'allIncomeTypeFundMapping')::boolean, FALSE);

            DELETE FROM funds fund
            WHERE fund."Id" IN (
                    '16c1e6d9-52f8-4c57-9b4f-1cbd819d3981'::uuid,
                    '25e7390f-50ca-4d6a-871c-acde1563bffc'::uuid,
                    '3acaa67a-dcb3-4027-a199-0a27dbe426c1'::uuid,
                    '4ced0dd4-1635-44ea-82f9-85a6cf6217af'::uuid)
              AND NOT EXISTS (
                  SELECT 1
                  FROM fund_operations operation
                  WHERE operation."FundId" = fund."Id");
            """);
    }
}
