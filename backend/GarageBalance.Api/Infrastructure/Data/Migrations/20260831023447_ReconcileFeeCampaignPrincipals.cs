using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileFeeCampaignPrincipals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH stable_destination AS (
                    SELECT income_type."Id"
                    FROM income_types AS income_type
                    WHERE income_type."Code" = 'other_income'
                      AND income_type."IsSystem" = TRUE
                      AND income_type."IsArchived" = FALSE
                    ORDER BY income_type."Id"
                    LIMIT 1
                )
                UPDATE fee_campaigns AS campaign
                SET
                    "IncomeTypeId" = destination."Id",
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM stable_destination AS destination
                WHERE campaign."IncomeTypeId" <> destination."Id";

                WITH stable_destination AS (
                    SELECT income_type."Id"
                    FROM income_types AS income_type
                    WHERE income_type."Code" = 'other_income'
                      AND income_type."IsSystem" = TRUE
                      AND income_type."IsArchived" = FALSE
                    ORDER BY income_type."Id"
                    LIMIT 1
                )
                UPDATE accruals AS accrual
                SET
                    "IncomeTypeId" = destination."Id",
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM stable_destination AS destination
                WHERE accrual."FeeCampaignId" IS NOT NULL
                  AND accrual."IsCanceled" = FALSE
                  AND accrual."IncomeTypeId" <> destination."Id";

                WITH first_tagged_payment AS (
                    SELECT DISTINCT ON (operation."GarageId", operation."FeeCampaignId")
                        operation."GarageId" AS garage_id,
                        campaign."IncomeTypeId" AS income_type_id,
                        operation."FeeCampaignId" AS campaign_id,
                        operation."AccountingMonth" AS accounting_month,
                        operation."CreatedAtUtc" AS created_at_utc,
                        campaign."Name" AS campaign_name,
                        campaign."EndsOn" AS ends_on,
                        campaign."OverdueGraceDays" AS overdue_grace_days
                    FROM financial_operations AS operation
                    INNER JOIN fee_campaigns AS campaign
                        ON campaign."Id" = operation."FeeCampaignId"
                    WHERE operation."IsCanceled" = FALSE
                      AND operation."OperationKind" = 'income'
                      AND operation."GarageId" IS NOT NULL
                      AND operation."IncomeTypeId" IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1
                          FROM accruals AS existing
                          WHERE existing."GarageId" = operation."GarageId"
                            AND existing."FeeCampaignId" = operation."FeeCampaignId")
                    ORDER BY
                        operation."GarageId",
                        operation."FeeCampaignId",
                        operation."OperationDate",
                        operation."CreatedAtUtc",
                        operation."Id"
                )
                INSERT INTO accruals
                    ("Id", "GarageId", "IncomeTypeId", "FeeCampaignId", "Basis",
                     "AccountingMonth", "DueDate", "OverdueFromDate", "Amount", "Source",
                     "Comment", "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    gen_random_uuid(),
                    payment.garage_id,
                    payment.income_type_id,
                    payment.campaign_id,
                    payment.campaign_name,
                    payment.accounting_month,
                    COALESCE(
                        payment.ends_on,
                        (payment.accounting_month + INTERVAL '1 month - 1 day')::date),
                    COALESCE(
                        payment.ends_on,
                        (payment.accounting_month + INTERVAL '1 month - 1 day')::date) +
                        (payment.overdue_grace_days + 1),
                    0,
                    'fee_campaign',
                    CASE
                        WHEN campaign."ClosedAtUtc" IS NOT NULL
                            THEN 'Восстановлено при сверке закрытого сбора'
                        ELSE 'Восстановлено при сверке открытого сбора'
                    END,
                    TRUE,
                    payment.created_at_utc,
                    CURRENT_TIMESTAMP
                FROM first_tagged_payment AS payment
                INNER JOIN fee_campaigns AS campaign
                    ON campaign."Id" = payment.campaign_id;

                CREATE TEMP TABLE closed_fee_campaign_accrual_map AS
                SELECT
                    accrual."Id" AS accrual_id,
                    first_value(accrual."Id") OVER (
                        PARTITION BY accrual."GarageId", accrual."FeeCampaignId"
                        ORDER BY accrual."IsCanceled", accrual."AccountingMonth", accrual."CreatedAtUtc", accrual."Id") AS principal_id,
                    accrual."GarageId" AS garage_id,
                    accrual."FeeCampaignId" AS campaign_id
                FROM accruals AS accrual
                INNER JOIN fee_campaigns AS campaign
                    ON campaign."Id" = accrual."FeeCampaignId"
                WHERE accrual."FeeCampaignId" IS NOT NULL
                  AND campaign."ClosedAtUtc" IS NOT NULL;

                CREATE TEMP TABLE closed_fee_campaign_repaired_allocations AS
                SELECT
                    source.principal_id,
                    source.financial_operation_id,
                    LEAST(SUM(source.amount), MAX(source.operation_amount)) AS amount,
                    MIN(source.created_at_utc) AS created_at_utc
                FROM (
                    SELECT DISTINCT
                        map.principal_id,
                        operation."Id" AS financial_operation_id,
                        operation."Amount" AS amount,
                        operation."Amount" AS operation_amount,
                        operation."CreatedAtUtc" AS created_at_utc
                    FROM closed_fee_campaign_accrual_map AS map
                    INNER JOIN financial_operations AS operation
                        ON operation."GarageId" = map.garage_id
                       AND operation."FeeCampaignId" = map.campaign_id
                    WHERE operation."IsCanceled" = FALSE
                      AND operation."OperationKind" = 'income'

                    UNION ALL

                    SELECT
                        map.principal_id,
                        allocation."FinancialOperationId" AS financial_operation_id,
                        allocation."Amount" AS amount,
                        operation."Amount" AS operation_amount,
                        allocation."CreatedAtUtc" AS created_at_utc
                    FROM closed_fee_campaign_accrual_map AS map
                    INNER JOIN accrual_payment_allocations AS allocation
                        ON allocation."AccrualId" = map.accrual_id
                    INNER JOIN financial_operations AS operation
                        ON operation."Id" = allocation."FinancialOperationId"
                    WHERE allocation."IsActive" = TRUE
                      AND operation."IsCanceled" = FALSE
                      AND operation."OperationKind" = 'income'
                      AND operation."FeeCampaignId" IS NULL
                ) AS source
                GROUP BY source.principal_id, source.financial_operation_id;

                CREATE TEMP TABLE closed_fee_campaign_paid AS
                SELECT
                    principals.principal_id,
                    COALESCE(SUM(repaired.amount), 0) AS paid_amount
                FROM (
                    SELECT DISTINCT principal_id
                    FROM closed_fee_campaign_accrual_map
                ) AS principals
                LEFT JOIN closed_fee_campaign_repaired_allocations AS repaired
                    ON repaired.principal_id = principals.principal_id
                GROUP BY principals.principal_id;

                CREATE TEMP TABLE open_fee_campaign_accrual_map AS
                SELECT
                    accrual."Id" AS accrual_id,
                    first_value(accrual."Id") OVER (
                        PARTITION BY accrual."GarageId", accrual."FeeCampaignId"
                        ORDER BY accrual."IsCanceled", accrual."AccountingMonth", accrual."CreatedAtUtc", accrual."Id") AS principal_id,
                    accrual."GarageId" AS garage_id,
                    accrual."FeeCampaignId" AS campaign_id
                FROM accruals AS accrual
                INNER JOIN fee_campaigns AS campaign
                    ON campaign."Id" = accrual."FeeCampaignId"
                WHERE accrual."FeeCampaignId" IS NOT NULL
                  AND campaign."ClosedAtUtc" IS NULL;

                CREATE TEMP TABLE open_fee_campaign_allocation_candidates AS
                SELECT
                    source.principal_id,
                    source.financial_operation_id,
                    LEAST(SUM(source.amount), MAX(source.operation_amount)) AS amount,
                    MIN(source.operation_date) AS operation_date,
                    MIN(source.operation_created_at_utc) AS operation_created_at_utc,
                    MIN(source.allocation_created_at_utc) AS allocation_created_at_utc
                FROM (
                    SELECT DISTINCT
                        map.principal_id,
                        operation."Id" AS financial_operation_id,
                        operation."Amount" AS amount,
                        operation."Amount" AS operation_amount,
                        operation."OperationDate" AS operation_date,
                        operation."CreatedAtUtc" AS operation_created_at_utc,
                        operation."CreatedAtUtc" AS allocation_created_at_utc
                    FROM open_fee_campaign_accrual_map AS map
                    INNER JOIN financial_operations AS operation
                        ON operation."GarageId" = map.garage_id
                       AND operation."FeeCampaignId" = map.campaign_id
                    WHERE operation."IsCanceled" = FALSE
                      AND operation."OperationKind" = 'income'

                    UNION ALL

                    SELECT
                        map.principal_id,
                        allocation."FinancialOperationId" AS financial_operation_id,
                        allocation."Amount" AS amount,
                        operation."Amount" AS operation_amount,
                        operation."OperationDate" AS operation_date,
                        operation."CreatedAtUtc" AS operation_created_at_utc,
                        allocation."CreatedAtUtc" AS allocation_created_at_utc
                    FROM open_fee_campaign_accrual_map AS map
                    INNER JOIN accrual_payment_allocations AS allocation
                        ON allocation."AccrualId" = map.accrual_id
                    INNER JOIN financial_operations AS operation
                        ON operation."Id" = allocation."FinancialOperationId"
                    WHERE allocation."IsActive" = TRUE
                      AND operation."IsCanceled" = FALSE
                      AND operation."OperationKind" = 'income'
                      AND operation."FeeCampaignId" IS NULL
                ) AS source
                GROUP BY source.principal_id, source.financial_operation_id;

                CREATE TEMP TABLE open_fee_campaign_principal_amounts AS
                SELECT
                    principals.principal_id,
                    GREATEST(
                        principal."Amount",
                        COALESCE(SUM(candidate.amount), 0)) AS desired_amount,
                    COALESCE(SUM(candidate.amount), 0) AS paid_amount
                FROM (
                    SELECT DISTINCT principal_id
                    FROM open_fee_campaign_accrual_map
                ) AS principals
                INNER JOIN accruals AS principal
                    ON principal."Id" = principals.principal_id
                LEFT JOIN open_fee_campaign_allocation_candidates AS candidate
                    ON candidate.principal_id = principals.principal_id
                GROUP BY principals.principal_id, principal."Amount";

                UPDATE accruals AS principal
                SET
                    "Amount" = amounts.desired_amount,
                    "IsCanceled" = FALSE,
                    "UpdatedAtUtc" = CASE
                        WHEN principal."Amount" <> amounts.desired_amount OR principal."IsCanceled"
                            THEN CURRENT_TIMESTAMP
                        ELSE principal."UpdatedAtUtc"
                    END
                FROM open_fee_campaign_principal_amounts AS amounts
                WHERE principal."Id" = amounts.principal_id;

                CREATE TEMP TABLE open_fee_campaign_repaired_allocations AS
                SELECT
                    ordered.principal_id,
                    ordered.financial_operation_id,
                    LEAST(
                        ordered.amount,
                        GREATEST(ordered.principal_amount - ordered.previously_allocated, 0)) AS amount,
                    ordered.allocation_created_at_utc AS created_at_utc
                FROM (
                    SELECT
                        candidate.*,
                        principal."Amount" AS principal_amount,
                        COALESCE(SUM(candidate.amount) OVER (
                            PARTITION BY candidate.principal_id
                            ORDER BY
                                candidate.operation_date,
                                candidate.operation_created_at_utc,
                                candidate.financial_operation_id
                            ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS previously_allocated
                    FROM open_fee_campaign_allocation_candidates AS candidate
                    INNER JOIN accruals AS principal
                        ON principal."Id" = candidate.principal_id
                ) AS ordered;

                UPDATE accrual_payment_allocations AS allocation
                SET "IsActive" = FALSE
                FROM open_fee_campaign_accrual_map AS map
                WHERE allocation."AccrualId" = map.accrual_id
                  AND allocation."IsActive" = TRUE;

                WITH ranked AS (
                    SELECT
                        accrual."Id",
                        row_number() OVER (
                            PARTITION BY accrual."GarageId", accrual."FeeCampaignId"
                            ORDER BY accrual."AccountingMonth", accrual."CreatedAtUtc", accrual."Id") AS row_number
                    FROM accruals AS accrual
                    WHERE accrual."IsCanceled" = FALSE
                      AND accrual."FeeCampaignId" IS NOT NULL
                )
                UPDATE accrual_payment_allocations AS allocation
                SET "IsActive" = FALSE
                FROM ranked
                WHERE ranked.row_number > 1
                  AND allocation."AccrualId" = ranked."Id"
                  AND allocation."IsActive" = TRUE;

                WITH ranked AS (
                    SELECT
                        accrual."Id",
                        row_number() OVER (
                            PARTITION BY accrual."GarageId", accrual."FeeCampaignId"
                            ORDER BY accrual."AccountingMonth", accrual."CreatedAtUtc", accrual."Id") AS row_number
                    FROM accruals AS accrual
                    WHERE accrual."IsCanceled" = FALSE
                      AND accrual."FeeCampaignId" IS NOT NULL
                )
                UPDATE accruals AS accrual
                SET
                    "IsCanceled" = TRUE,
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM ranked
                WHERE ranked.row_number > 1
                  AND accrual."Id" = ranked."Id";

                INSERT INTO accrual_payment_allocations
                    ("Id", "FinancialOperationId", "AccrualId", "Amount", "IsActive", "CreatedAtUtc")
                SELECT
                    gen_random_uuid(),
                    repaired.financial_operation_id,
                    repaired.principal_id,
                    repaired.amount,
                    TRUE,
                    repaired.created_at_utc
                FROM open_fee_campaign_repaired_allocations AS repaired
                WHERE repaired.amount > 0;

                UPDATE accruals AS accrual
                SET
                    "IsCanceled" = TRUE,
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM closed_fee_campaign_accrual_map AS map
                WHERE accrual."Id" = map.accrual_id
                  AND map.accrual_id <> map.principal_id
                  AND accrual."IsCanceled" = FALSE;

                UPDATE accruals AS accrual
                SET
                    "Amount" = closed_fee_campaign_paid.paid_amount,
                    "IsCanceled" = closed_fee_campaign_paid.paid_amount <= 0,
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM closed_fee_campaign_paid
                WHERE accrual."Id" = closed_fee_campaign_paid.principal_id;

                UPDATE accrual_payment_allocations AS allocation
                SET "IsActive" = FALSE
                FROM closed_fee_campaign_accrual_map AS map
                WHERE allocation."AccrualId" = map.accrual_id
                  AND allocation."IsActive" = TRUE;

                INSERT INTO accrual_payment_allocations
                    ("Id", "FinancialOperationId", "AccrualId", "Amount", "IsActive", "CreatedAtUtc")
                SELECT
                    gen_random_uuid(),
                    repaired.financial_operation_id,
                    repaired.principal_id,
                    repaired.amount,
                    TRUE,
                    repaired.created_at_utc
                FROM closed_fee_campaign_repaired_allocations AS repaired
                INNER JOIN accruals AS principal
                    ON principal."Id" = repaired.principal_id
                WHERE principal."IsCanceled" = FALSE
                  AND repaired.amount > 0;

                UPDATE accrual_payment_allocations AS allocation
                SET "IsActive" = FALSE
                FROM accruals AS accrual
                WHERE allocation."AccrualId" = accrual."Id"
                  AND allocation."IsActive" = TRUE
                  AND accrual."IsCanceled" = TRUE
                  AND accrual."FeeCampaignId" IS NOT NULL;

                UPDATE accruals AS accrual
                SET
                    "IncomeTypeId" = campaign."IncomeTypeId",
                    "DueDate" = COALESCE(
                        campaign."EndsOn",
                        (accrual."AccountingMonth" + INTERVAL '1 month - 1 day')::date),
                    "OverdueFromDate" = COALESCE(
                        campaign."EndsOn",
                        (accrual."AccountingMonth" + INTERVAL '1 month - 1 day')::date) +
                        (campaign."OverdueGraceDays" + 1),
                    "DueDateNeedsReview" = FALSE,
                    "DueDateReviewReason" = NULL,
                    "UpdatedAtUtc" = CURRENT_TIMESTAMP
                FROM fee_campaigns AS campaign
                WHERE accrual."FeeCampaignId" = campaign."Id"
                  AND accrual."IsCanceled" = FALSE;

                DO $$
                DECLARE
                    allocation_key RECORD;
                    payment RECORD;
                    ordinary_accrual RECORD;
                    payment_remaining NUMERIC;
                    accrual_remaining NUMERIC;
                    allocated_amount NUMERIC;
                BEGIN
                    FOR allocation_key IN
                        SELECT DISTINCT
                            operation."GarageId" AS garage_id,
                            operation."IncomeTypeId" AS income_type_id
                        FROM financial_operations AS operation
                        INNER JOIN accruals AS accrual
                            ON accrual."GarageId" = operation."GarageId"
                           AND accrual."IncomeTypeId" = operation."IncomeTypeId"
                        WHERE operation."IsCanceled" = FALSE
                          AND operation."OperationKind" = 'income'
                          AND operation."GarageId" IS NOT NULL
                          AND operation."IncomeTypeId" IS NOT NULL
                          AND operation."FeeCampaignId" IS NULL
                          AND operation."IrregularPaymentId" IS NULL
                          AND accrual."IsCanceled" = FALSE
                          AND accrual."DueDateNeedsReview" = FALSE
                          AND accrual."FeeCampaignId" IS NULL
                          AND accrual."IrregularPaymentId" IS NULL
                    LOOP
                        UPDATE accrual_payment_allocations AS allocation
                        SET "IsActive" = FALSE
                        FROM financial_operations AS operation,
                             accruals AS accrual
                        WHERE allocation."FinancialOperationId" = operation."Id"
                          AND allocation."AccrualId" = accrual."Id"
                          AND allocation."IsActive" = TRUE
                          AND operation."IsCanceled" = FALSE
                          AND operation."OperationKind" = 'income'
                          AND operation."GarageId" = allocation_key.garage_id
                          AND operation."IncomeTypeId" = allocation_key.income_type_id
                          AND operation."FeeCampaignId" IS NULL
                          AND operation."IrregularPaymentId" IS NULL
                          AND accrual."GarageId" = allocation_key.garage_id
                          AND accrual."IncomeTypeId" = allocation_key.income_type_id
                          AND accrual."FeeCampaignId" IS NULL
                          AND accrual."IrregularPaymentId" IS NULL;

                        FOR payment IN
                            SELECT
                                operation."Id" AS operation_id,
                                operation."OperationDate" AS operation_date,
                                operation."AccountingMonth" AS accounting_month,
                                operation."CreatedAtUtc" AS created_at_utc,
                                GREATEST(
                                    operation."Amount" - COALESCE((
                                        SELECT SUM(existing."Amount")
                                        FROM accrual_payment_allocations AS existing
                                        INNER JOIN accruals AS targeted_accrual
                                            ON targeted_accrual."Id" = existing."AccrualId"
                                        WHERE existing."FinancialOperationId" = operation."Id"
                                          AND existing."IsActive" = TRUE
                                          AND targeted_accrual."IsCanceled" = FALSE
                                          AND (
                                              targeted_accrual."FeeCampaignId" IS NOT NULL
                                              OR targeted_accrual."IrregularPaymentId" IS NOT NULL)
                                    ), 0),
                                    0) AS available_amount
                            FROM financial_operations AS operation
                            WHERE operation."IsCanceled" = FALSE
                              AND operation."OperationKind" = 'income'
                              AND operation."GarageId" = allocation_key.garage_id
                              AND operation."IncomeTypeId" = allocation_key.income_type_id
                              AND operation."FeeCampaignId" IS NULL
                              AND operation."IrregularPaymentId" IS NULL
                            ORDER BY
                                operation."OperationDate",
                                operation."CreatedAtUtc",
                                operation."Id"
                        LOOP
                            payment_remaining := payment.available_amount;

                            FOR ordinary_accrual IN
                                SELECT
                                    accrual."Id" AS accrual_id,
                                    accrual."Amount" AS amount
                                FROM accruals AS accrual
                                WHERE accrual."IsCanceled" = FALSE
                                  AND accrual."DueDateNeedsReview" = FALSE
                                  AND accrual."GarageId" = allocation_key.garage_id
                                  AND accrual."IncomeTypeId" = allocation_key.income_type_id
                                  AND accrual."FeeCampaignId" IS NULL
                                  AND accrual."IrregularPaymentId" IS NULL
                                ORDER BY
                                    CASE
                                        WHEN accrual."AccountingMonth" = payment.accounting_month THEN 0
                                        ELSE 1
                                    END,
                                    accrual."DueDate",
                                    accrual."AccountingMonth",
                                    accrual."CreatedAtUtc",
                                    accrual."Id"
                            LOOP
                                EXIT WHEN payment_remaining <= 0;

                                SELECT GREATEST(
                                    ordinary_accrual.amount - COALESCE(SUM(existing."Amount"), 0),
                                    0)
                                INTO accrual_remaining
                                FROM accrual_payment_allocations AS existing
                                WHERE existing."AccrualId" = ordinary_accrual.accrual_id
                                  AND existing."IsActive" = TRUE;

                                allocated_amount := LEAST(payment_remaining, accrual_remaining);
                                IF allocated_amount > 0 THEN
                                    INSERT INTO accrual_payment_allocations
                                        ("Id", "FinancialOperationId", "AccrualId", "Amount", "IsActive", "CreatedAtUtc")
                                    VALUES
                                        (gen_random_uuid(), payment.operation_id, ordinary_accrual.accrual_id,
                                         allocated_amount, TRUE, CURRENT_TIMESTAMP);
                                    payment_remaining := payment_remaining - allocated_amount;
                                END IF;
                            END LOOP;
                        END LOOP;
                    END LOOP;
                END $$;

                DROP TABLE closed_fee_campaign_paid;
                DROP TABLE closed_fee_campaign_repaired_allocations;
                DROP TABLE closed_fee_campaign_accrual_map;
                DROP TABLE open_fee_campaign_repaired_allocations;
                DROP TABLE open_fee_campaign_principal_amounts;
                DROP TABLE open_fee_campaign_allocation_candidates;
                DROP TABLE open_fee_campaign_accrual_map;
                """);

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_FeeCampaignId_AccountingMonth",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_FeeCampaignId",
                table: "accruals",
                columns: new[] { "GarageId", "FeeCampaignId" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"FeeCampaignId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_FeeCampaignId",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_FeeCampaignId_AccountingMonth",
                table: "accruals",
                columns: new[] { "GarageId", "FeeCampaignId", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"FeeCampaignId\" IS NOT NULL");
        }
    }
}
