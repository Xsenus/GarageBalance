using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BalanceStagingDemoGarages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo_balances$
                DECLARE
                    balance_audit_id uuid := md5('garagebalance-staging-demo-balances-v2')::uuid;
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) <> 'on' THEN
                        RETURN;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM audit_events
                        WHERE "Id" = md5('garagebalance-staging-demo-dataset-v1')::uuid)
                       OR EXISTS (
                        SELECT 1
                        FROM audit_events
                        WHERE "Id" = balance_audit_id) THEN
                        RETURN;
                    END IF;

                    WITH demo_garages AS (
                        SELECT
                            garage_no,
                            md5('garagebalance-demo-garage-' || garage_no)::uuid AS garage_id,
                            CASE
                                WHEN garage_no BETWEEN 107 AND 113 THEN 0.00::numeric(18,2)
                                ELSE (-1000.00 - ((garage_no - 114) * 250.00))::numeric(18,2)
                            END AS target_debt
                        FROM generate_series(107, 120) AS garages(garage_no)
                    ),
                    active_accruals AS (
                        SELECT "GarageId", sum("Amount")::numeric(18,2) AS amount
                        FROM accruals
                        WHERE NOT "IsCanceled"
                          AND "GarageId" IN (SELECT garage_id FROM demo_garages)
                        GROUP BY "GarageId"
                    ),
                    active_income AS (
                        SELECT "GarageId", sum("Amount")::numeric(18,2) AS amount
                        FROM financial_operations
                        WHERE "OperationKind" = 'income'
                          AND NOT "IsCanceled"
                          AND "GarageId" IN (SELECT garage_id FROM demo_garages)
                        GROUP BY "GarageId"
                    ),
                    payment_adjustments AS (
                        SELECT
                            demo.garage_no,
                            demo.garage_id,
                            round(
                                garage."StartingBalance"
                                + coalesce(accrual.amount, 0)
                                - coalesce(income.amount, 0)
                                - demo.target_debt,
                                2) AS amount
                        FROM demo_garages demo
                        INNER JOIN garages garage ON garage."Id" = demo.garage_id
                        LEFT JOIN active_accruals accrual ON accrual."GarageId" = demo.garage_id
                        LEFT JOIN active_income income ON income."GarageId" = demo.garage_id
                    )
                    INSERT INTO financial_operations (
                        "Id", "OperationKind", "OperationDate", "AccountingMonth", "Amount", "DocumentNumber",
                        "Comment", "GarageId", "IncomeTypeId", "SupplierId", "StaffMemberId", "ExpenseTypeId",
                        "IsCanceled", "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        md5('garagebalance-demo-balance-adjustment-' || adjustment.garage_no)::uuid,
                        'income',
                        DATE '2026-07-17',
                        DATE '2026-07-01',
                        adjustment.amount,
                        'ДЕМО-БАЛ-' || adjustment.garage_no,
                        CASE
                            WHEN adjustment.garage_no <= 113
                                THEN 'Демонстрационная полная оплата задолженности.'
                            ELSE 'Демонстрационная оплата с авансом для проверки положительного остатка.'
                        END,
                        adjustment.garage_id,
                        income_type."Id",
                        NULL,
                        NULL,
                        NULL,
                        FALSE,
                        TIMESTAMPTZ '2026-07-17T02:10:00Z',
                        TIMESTAMPTZ '2026-07-17T02:10:00Z'
                    FROM payment_adjustments adjustment
                    CROSS JOIN LATERAL (
                        SELECT "Id"
                        FROM income_types
                        WHERE "Code" = 'membership' AND NOT "IsArchived"
                        ORDER BY "Id"
                        LIMIT 1) income_type
                    WHERE adjustment.amount > 0
                    ON CONFLICT DO NOTHING;

                    INSERT INTO audit_events (
                        "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                        "EntityDisplayName", "Summary", "MetadataJson")
                    VALUES (
                        balance_audit_id,
                        TIMESTAMPTZ '2026-07-17T02:10:00Z',
                        'demo.dataset.balances.diversified',
                        'settings',
                        'update',
                        'demo_dataset',
                        'staging-demo-v2',
                        'Разнообразные балансы демонстрационных гаражей',
                        'В демонстрационном наборе представлены гаражи с задолженностью, полной оплатой и авансом.',
                        jsonb_build_object(
                            'debtGarageNumbers', '101-106',
                            'paidGarageNumbers', '107-113',
                            'advanceGarageNumbers', '114-120',
                            'containsRealPersonalData', false)::text);
                END $demo_balances$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo_balances$
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) <> 'on' THEN
                        RETURN;
                    END IF;

                    DELETE FROM financial_operations
                    WHERE "Id" IN (
                        SELECT md5('garagebalance-demo-balance-adjustment-' || garage_no)::uuid
                        FROM generate_series(107, 120) AS garages(garage_no));

                    DELETE FROM audit_events
                    WHERE "Id" = md5('garagebalance-staging-demo-balances-v2')::uuid;
                END $demo_balances$;
                """);
        }
    }
}
