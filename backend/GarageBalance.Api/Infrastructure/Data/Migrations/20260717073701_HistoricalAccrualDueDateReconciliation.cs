using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalAccrualDueDateReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DueDateNeedsReview",
                table: "accruals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DueDateReviewReason",
                table: "accruals",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    WITH unique_settings AS (
                        SELECT
                            "IncomeTypeId",
                            MAX("PeriodicityMonths") AS periodicity_months,
                            MAX("PaymentDueDay") AS payment_due_day,
                            MAX("PaymentDueMonth") AS payment_due_month,
                            MAX("OverdueGraceDays") AS overdue_grace_days
                        FROM charge_service_settings
                        WHERE "IncomeTypeId" IS NOT NULL
                        GROUP BY "IncomeTypeId"
                        HAVING COUNT(*) = 1
                    ),
                    calculated AS (
                        SELECT
                            a."Id",
                            CASE
                                WHEN s.periodicity_months >= 12
                                 AND s.payment_due_day IS NOT NULL
                                 AND s.payment_due_month IS NOT NULL
                                THEN CASE
                                    WHEN make_date(
                                        EXTRACT(YEAR FROM a."AccountingMonth")::int,
                                        s.payment_due_month,
                                        LEAST(s.payment_due_day, EXTRACT(DAY FROM (make_date(EXTRACT(YEAR FROM a."AccountingMonth")::int, s.payment_due_month, 1) + INTERVAL '1 month - 1 day'))::int)) < a."AccountingMonth"
                                    THEN make_date(
                                        EXTRACT(YEAR FROM a."AccountingMonth")::int + 1,
                                        s.payment_due_month,
                                        LEAST(s.payment_due_day, EXTRACT(DAY FROM (make_date(EXTRACT(YEAR FROM a."AccountingMonth")::int + 1, s.payment_due_month, 1) + INTERVAL '1 month - 1 day'))::int))
                                    ELSE make_date(
                                        EXTRACT(YEAR FROM a."AccountingMonth")::int,
                                        s.payment_due_month,
                                        LEAST(s.payment_due_day, EXTRACT(DAY FROM (make_date(EXTRACT(YEAR FROM a."AccountingMonth")::int, s.payment_due_month, 1) + INTERVAL '1 month - 1 day'))::int))
                                END
                                ELSE make_date(
                                    EXTRACT(YEAR FROM (a."AccountingMonth" + INTERVAL '1 month'))::int,
                                    EXTRACT(MONTH FROM (a."AccountingMonth" + INTERVAL '1 month'))::int,
                                    LEAST(
                                        COALESCE(s.payment_due_day, EXTRACT(DAY FROM (date_trunc('month', a."AccountingMonth" + INTERVAL '2 months') - INTERVAL '1 day'))::int),
                                        EXTRACT(DAY FROM (date_trunc('month', a."AccountingMonth" + INTERVAL '2 months') - INTERVAL '1 day'))::int))
                            END AS due_date,
                            s.overdue_grace_days
                        FROM accruals a
                        JOIN unique_settings s ON s."IncomeTypeId" = a."IncomeTypeId"
                        WHERE a."Source" = 'regular'
                    )
                    UPDATE accruals a
                    SET "DueDate" = calculated.due_date,
                        "OverdueFromDate" = calculated.due_date + (calculated.overdue_grace_days + 1),
                        "DueDateNeedsReview" = FALSE,
                        "DueDateReviewReason" = NULL
                    FROM calculated
                    WHERE a."Id" = calculated."Id";

                    WITH matching_campaigns AS (
                        SELECT
                            a."Id" AS accrual_id,
                            COUNT(c."Id") AS campaign_count,
                            MAX(c."EndsOn") AS ends_on,
                            MAX(c."OverdueGraceDays") AS overdue_grace_days
                        FROM accruals a
                        LEFT JOIN fee_campaigns c
                          ON c."IncomeTypeId" = a."IncomeTypeId"
                         AND a."AccountingMonth" >= date_trunc('month', c."StartsOn")::date
                         AND (c."EndsOn" IS NULL OR a."AccountingMonth" <= date_trunc('month', c."EndsOn")::date)
                        WHERE a."Source" = 'fee_campaign'
                        GROUP BY a."Id"
                    ),
                    calculated AS (
                        SELECT
                            a."Id",
                            COALESCE(m.ends_on, (date_trunc('month', a."AccountingMonth") + INTERVAL '1 month - 1 day')::date) AS due_date,
                            m.overdue_grace_days
                        FROM accruals a
                        JOIN matching_campaigns m ON m.accrual_id = a."Id" AND m.campaign_count = 1
                    )
                    UPDATE accruals a
                    SET "DueDate" = calculated.due_date,
                        "OverdueFromDate" = calculated.due_date + (calculated.overdue_grace_days + 1),
                        "DueDateNeedsReview" = FALSE,
                        "DueDateReviewReason" = NULL
                    FROM calculated
                    WHERE a."Id" = calculated."Id";

                    UPDATE accruals a
                    SET "DueDateNeedsReview" = TRUE,
                        "DueDateReviewReason" = 'regular_service_not_unique'
                    WHERE a."Source" = 'regular'
                      AND (SELECT COUNT(*) FROM charge_service_settings s WHERE s."IncomeTypeId" = a."IncomeTypeId") <> 1;

                    UPDATE accruals a
                    SET "DueDateNeedsReview" = TRUE,
                        "DueDateReviewReason" = 'fee_campaign_not_unique'
                    WHERE a."Source" = 'fee_campaign'
                      AND (
                          SELECT COUNT(*)
                          FROM fee_campaigns c
                          WHERE c."IncomeTypeId" = a."IncomeTypeId"
                            AND a."AccountingMonth" >= date_trunc('month', c."StartsOn")::date
                            AND (c."EndsOn" IS NULL OR a."AccountingMonth" <= date_trunc('month', c."EndsOn")::date)
                      ) <> 1;

                    UPDATE accruals
                    SET "DueDateNeedsReview" = TRUE,
                        "DueDateReviewReason" = 'historical_source_unknown'
                    WHERE "Source" NOT IN ('manual', 'regular', 'fee_campaign', 'debt_transfer');

                    UPDATE accrual_payment_allocations allocation
                    SET "IsActive" = FALSE
                    FROM accruals accrual
                    WHERE allocation."AccrualId" = accrual."Id"
                      AND allocation."IsActive" = TRUE
                      AND accrual."DueDateNeedsReview" = TRUE;
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_accruals_DueDateNeedsReview_AccountingMonth",
                table: "accruals",
                columns: new[] { "DueDateNeedsReview", "AccountingMonth" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_DueDateNeedsReview_AccountingMonth",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "DueDateNeedsReview",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "DueDateReviewReason",
                table: "accruals");
        }
    }
}
