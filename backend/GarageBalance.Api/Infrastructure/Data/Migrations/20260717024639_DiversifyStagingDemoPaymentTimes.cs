using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DiversifyStagingDemoPaymentTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo_payment_times$
                DECLARE
                    payment_times_audit_id uuid := md5('garagebalance-staging-demo-payment-times-v3')::uuid;
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
                        WHERE "Id" = payment_times_audit_id) THEN
                        RETURN;
                    END IF;

                    WITH demo_operation_ids AS (
                        SELECT md5('garagebalance-demo-income-' || accrual."Id")::uuid AS operation_id
                        FROM accruals accrual
                        WHERE accrual."GarageId" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS garages(garage_no))
                        UNION ALL
                        SELECT md5('garagebalance-demo-balance-adjustment-' || garage_no)::uuid
                        FROM generate_series(107, 120) AS garages(garage_no)
                    )
                    UPDATE financial_operations operation
                    SET "CreatedAtUtc" = make_timestamptz(
                        extract(year FROM operation."OperationDate")::integer,
                        extract(month FROM operation."OperationDate")::integer,
                        extract(day FROM operation."OperationDate")::integer,
                        8 + mod(abs(hashtext(operation."Id"::text)::bigint), 12)::integer,
                        (mod(abs(hashtext(reverse(operation."Id"::text))::bigint), 12) * 5)::integer,
                        0,
                        'Asia/Novosibirsk')
                    FROM demo_operation_ids demo
                    WHERE operation."Id" = demo.operation_id;

                    INSERT INTO audit_events (
                        "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                        "EntityDisplayName", "Summary", "MetadataJson")
                    VALUES (
                        payment_times_audit_id,
                        TIMESTAMPTZ '2026-07-17T02:46:39Z',
                        'demo.dataset.payment_times.diversified',
                        'settings',
                        'update',
                        'demo_dataset',
                        'staging-demo-v3',
                        'Разнообразное время демонстрационных платежей',
                        'Время демонстрационных поступлений распределено по рабочему дню без изменения сумм, дат и расчётных месяцев.',
                        jsonb_build_object(
                            'localTimeFrom', '08:00',
                            'localTimeTo', '19:55',
                            'timeZone', 'Asia/Novosibirsk',
                            'containsRealPersonalData', false)::text);
                END $demo_payment_times$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $demo_payment_times$
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) <> 'on' THEN
                        RETURN;
                    END IF;

                    WITH demo_income_operation_ids AS (
                        SELECT md5('garagebalance-demo-income-' || accrual."Id")::uuid AS operation_id
                        FROM accruals accrual
                        WHERE accrual."GarageId" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS garages(garage_no))
                    )
                    UPDATE financial_operations operation
                    SET "CreatedAtUtc" = (operation."AccountingMonth" + INTERVAL '20 days')::timestamptz
                    FROM demo_income_operation_ids demo
                    WHERE operation."Id" = demo.operation_id;

                    UPDATE financial_operations operation
                    SET "CreatedAtUtc" = TIMESTAMPTZ '2026-07-17T02:10:00Z'
                    WHERE operation."Id" IN (
                        SELECT md5('garagebalance-demo-balance-adjustment-' || garage_no)::uuid
                        FROM generate_series(107, 120) AS garages(garage_no));

                    DELETE FROM audit_events
                    WHERE "Id" = md5('garagebalance-staging-demo-payment-times-v3')::uuid;
                END $demo_payment_times$;
                """);
        }
    }
}
