using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDemoDatasetFromWorkingDatabases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $cleanup_demo$
                BEGIN
                    IF lower(COALESCE(current_setting('garagebalance.demo_seed_enabled', true), 'off')) = 'on' THEN
                        RETURN;
                    END IF;

                    CREATE TEMP TABLE gb_demo_meter_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
                    INSERT INTO gb_demo_meter_ids ("Id")
                    SELECT md5(format(
                        'garagebalance-demo-meter-%s-%s-%s',
                        garage_no,
                        meter_kind,
                        to_char(accounting_month, 'YYYYMM')))::uuid
                    FROM generate_series(101, 120) AS garages(garage_no)
                    CROSS JOIN (VALUES ('water'), ('electricity')) AS kinds(meter_kind)
                    CROSS JOIN generate_series(
                        DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS months(accounting_month);

                    CREATE TEMP TABLE gb_demo_accrual_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
                    INSERT INTO gb_demo_accrual_ids ("Id")
                    SELECT md5(format(
                        'garagebalance-demo-accrual-%s-%s-%s',
                        garage_no,
                        service_code,
                        to_char(accounting_month, 'YYYYMM')))::uuid
                    FROM generate_series(101, 120) AS garages(garage_no)
                    CROSS JOIN (VALUES ('water'), ('electricity'), ('trash')) AS services(service_code)
                    CROSS JOIN generate_series(
                        DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS months(accounting_month)
                    UNION ALL
                    SELECT md5(format(
                        'garagebalance-demo-accrual-%s-membership-%s',
                        garage_no,
                        to_char(make_date(year_value, 6, 1), 'YYYYMM')))::uuid
                    FROM generate_series(101, 120) AS garages(garage_no)
                    CROSS JOIN generate_series(2021, 2026) AS years(year_value)
                    UNION ALL
                    SELECT md5(format(
                        'garagebalance-demo-accrual-%s-target-%s',
                        garage_no,
                        to_char(make_date(year_value, 3, 1), 'YYYYMM')))::uuid
                    FROM generate_series(101, 120) AS garages(garage_no)
                    CROSS JOIN generate_series(2021, 2026) AS years(year_value);

                    CREATE TEMP TABLE gb_demo_supplier_accrual_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
                    INSERT INTO gb_demo_supplier_accrual_ids ("Id")
                    SELECT md5(format(
                        'garagebalance-demo-supplier-accrual-%s-%s',
                        supplier_no,
                        to_char(accounting_month, 'YYYYMM')))::uuid
                    FROM generate_series(1, 5) AS suppliers(supplier_no)
                    CROSS JOIN generate_series(
                        DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS months(accounting_month);

                    CREATE TEMP TABLE gb_demo_operation_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
                    INSERT INTO gb_demo_operation_ids ("Id")
                    SELECT md5('garagebalance-demo-income-' || "Id")::uuid
                    FROM gb_demo_accrual_ids
                    UNION ALL
                    SELECT md5('garagebalance-demo-supplier-expense-' || "Id")::uuid
                    FROM gb_demo_supplier_accrual_ids
                    UNION ALL
                    SELECT md5(format(
                        'garagebalance-demo-salary-%s-%s',
                        md5('garagebalance-demo-staff-' || staff_no)::uuid,
                        to_char(accounting_month, 'YYYYMM')))::uuid
                    FROM generate_series(1, 7) AS staff(staff_no)
                    CROSS JOIN generate_series(
                        DATE '2021-01-01', DATE '2026-07-01', INTERVAL '1 month') AS months(accounting_month)
                    UNION ALL
                    SELECT md5('garagebalance-demo-balance-adjustment-' || garage_no)::uuid
                    FROM generate_series(107, 120) AS garages(garage_no);

                    CREATE TEMP TABLE gb_demo_fund_ids ("Id" uuid PRIMARY KEY) ON COMMIT DROP;
                    INSERT INTO gb_demo_fund_ids ("Id")
                    SELECT DISTINCT operation."FundId"
                    FROM fund_operations operation
                    WHERE operation."SourceFinancialOperationId" IN (
                        SELECT "Id" FROM gb_demo_operation_ids);

                    DELETE FROM audit_events audit
                    WHERE audit."Id" IN (
                            SELECT md5(operation."Id"::text || ':income-fund-assignment-audit')::uuid
                            FROM gb_demo_operation_ids operation)
                       OR audit."Id" IN (
                            md5('garagebalance-staging-demo-dataset-v1')::uuid,
                            md5('garagebalance-staging-demo-balances-v2')::uuid,
                            md5('garagebalance-staging-demo-payment-times-v3')::uuid);

                    DELETE FROM fund_operations operation
                    WHERE operation."SourceFinancialOperationId" IN (
                        SELECT "Id" FROM gb_demo_operation_ids);

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
                          AND operation."FundId" IN (SELECT "Id" FROM gb_demo_fund_ids)
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
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T00:29:08Z'
                    WHERE fund."Id" IN (SELECT "Id" FROM gb_demo_fund_ids);

                    DELETE FROM financial_operations
                    WHERE "Id" IN (SELECT "Id" FROM gb_demo_operation_ids);

                    DELETE FROM supplier_accruals
                    WHERE "Id" IN (SELECT "Id" FROM gb_demo_supplier_accrual_ids);

                    DELETE FROM accruals
                    WHERE "Id" IN (SELECT "Id" FROM gb_demo_accrual_ids);

                    DELETE FROM meter_readings
                    WHERE "Id" IN (SELECT "Id" FROM gb_demo_meter_ids);

                    DELETE FROM suppliers supplier
                    WHERE supplier."Id" IN (
                            SELECT md5('garagebalance-demo-supplier-' || supplier_no)::uuid
                            FROM generate_series(1, 5) AS ids(supplier_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM financial_operations operation
                            WHERE operation."SupplierId" = supplier."Id")
                      AND NOT EXISTS (
                            SELECT 1 FROM supplier_accruals accrual
                            WHERE accrual."SupplierId" = supplier."Id");

                    DELETE FROM staff_members staff
                    WHERE staff."Id" IN (
                            SELECT md5('garagebalance-demo-staff-' || staff_no)::uuid
                            FROM generate_series(1, 7) AS ids(staff_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM financial_operations operation
                            WHERE operation."StaffMemberId" = staff."Id");

                    DELETE FROM garages garage
                    WHERE garage."Id" IN (
                            SELECT md5('garagebalance-demo-garage-' || garage_no)::uuid
                            FROM generate_series(101, 120) AS ids(garage_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM financial_operations operation
                            WHERE operation."GarageId" = garage."Id")
                      AND NOT EXISTS (
                            SELECT 1 FROM accruals accrual
                            WHERE accrual."GarageId" = garage."Id")
                      AND NOT EXISTS (
                            SELECT 1 FROM meter_readings reading
                            WHERE reading."GarageId" = garage."Id");

                    DELETE FROM owners owner
                    WHERE owner."Id" IN (
                            SELECT md5('garagebalance-demo-owner-' || owner_no)::uuid
                            FROM generate_series(1, 20) AS ids(owner_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM garages garage
                            WHERE garage."OwnerId" = owner."Id");

                    DELETE FROM supplier_groups group_record
                    WHERE group_record."Id" IN (
                            SELECT md5('garagebalance-demo-supplier-group-' || group_no)::uuid
                            FROM generate_series(1, 5) AS ids(group_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM suppliers supplier
                            WHERE supplier."GroupId" = group_record."Id");

                    DELETE FROM staff_departments department
                    WHERE department."Id" IN (
                            SELECT md5('garagebalance-demo-department-' || department_no)::uuid
                            FROM generate_series(1, 4) AS ids(department_no))
                      AND NOT EXISTS (
                            SELECT 1 FROM staff_members staff
                            WHERE staff."DepartmentId" = department."Id");
                END $cleanup_demo$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: rolling back schema must never recreate demo business data.
        }
    }
}
