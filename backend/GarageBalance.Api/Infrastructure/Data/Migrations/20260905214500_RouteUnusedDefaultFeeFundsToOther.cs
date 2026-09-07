using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260905214500_RouteUnusedDefaultFeeFundsToOther")]
public sealed class RouteUnusedDefaultFeeFundsToOther : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $defaults$
            DECLARE
                other_fund_id uuid;
                candidate record;
            BEGIN
                SELECT "Id" INTO other_fund_id
                FROM funds
                WHERE "Id" = '58bc1538-5f77-46ce-9edf-8ed6aa73a701'::uuid
                  AND "NormalizedName" = 'ПРОЧЕЕ' AND "Name" = 'Прочее'
                  AND "IsSystem" AND NOT "IsArchived" AND "AllowOperations";
                IF other_fund_id IS NULL THEN RETURN; END IF;

                FOR candidate IN
                    SELECT fund."Id" AS fund_id, fund."Name" AS fund_name,
                           income_type."Id" AS income_type_id, income_type."Name" AS income_type_name
                    FROM (VALUES
                        ('718b671d-9133-4ffd-a130-3f2f03a8d150'::uuid, 'Членские взносы', 'ЧЛЕНСКИЕ ВЗНОСЫ', 50, 'membership'),
                        ('edc66ae5-29a3-4dbd-921e-4a1958612760'::uuid, 'Целевые взносы', 'ЦЕЛЕВЫЕ ВЗНОСЫ', 60, 'target')
                    ) AS defaults(id, name, normalized_name, sort_order, code)
                    JOIN funds fund ON fund."Id" = defaults.id
                    JOIN income_types income_type ON income_type."DestinationFundId" = fund."Id"
                    WHERE fund."Name" = defaults.name AND fund."NormalizedName" = defaults.normalized_name
                      AND fund."SortOrder" = defaults.sort_order AND fund."IsSystem"
                      AND fund."AllowOperations" AND NOT fund."IsArchived" AND fund."Balance" = 0
                      AND fund."CreatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
                      AND income_type."Code" = defaults.code AND income_type."IsSystem" AND NOT income_type."IsArchived"
                      AND income_type."UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T16:15:00Z'
                      AND (SELECT count(*) FROM income_types linked WHERE linked."DestinationFundId" = fund."Id") = 1
                      AND NOT EXISTS (SELECT 1 FROM fund_operations operation WHERE operation."FundId" = fund."Id")
                      AND NOT EXISTS (SELECT 1 FROM financial_operations operation WHERE operation."ExpenseFundId" = fund."Id")
                      AND NOT EXISTS (SELECT 1 FROM supplier_accruals accrual WHERE accrual."ExpenseFundId" = fund."Id")
                      AND NOT EXISTS (SELECT 1 FROM suppliers supplier WHERE supplier."ExpenseFundId" = fund."Id")
                      AND NOT EXISTS (SELECT 1 FROM audit_events audit
                          WHERE (audit."EntityType" = 'fund' AND audit."EntityId" = fund."Id"::text)
                             OR (audit."EntityType" = 'income_type' AND audit."EntityId" = income_type."Id"::text))
                    FOR UPDATE OF fund, income_type
                LOOP
                    UPDATE income_types SET "DestinationFundId" = other_fund_id, "UpdatedAtUtc" = NOW()
                    WHERE "Id" = candidate.income_type_id;

                    DELETE FROM funds WHERE "Id" = candidate.fund_id;

                    INSERT INTO audit_events (
                        "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                        "EntityDisplayName", "Summary", "MetadataJson")
                    VALUES (
                        md5(candidate.income_type_id::text || ':unused-default-fee-fund-to-other')::uuid,
                        NOW(), 'dictionary.default_fee_fund_changed', 'dictionaries', 'update', 'income_type',
                        candidate.income_type_id::text, candidate.income_type_name,
                        'Неиспользованное исходное назначение взноса изменено на фонд «Прочее».',
                        jsonb_build_object('previousFundId', candidate.fund_id, 'previousFundName', candidate.fund_name,
                            'destinationFundId', other_fund_id, 'removedUnusedDefaultFund', TRUE)::text);
                END LOOP;
            END $defaults$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The old application supports these destinations. Do not recreate unused funds
        // or overwrite settings that administrators may have changed after the upgrade.
    }
}
