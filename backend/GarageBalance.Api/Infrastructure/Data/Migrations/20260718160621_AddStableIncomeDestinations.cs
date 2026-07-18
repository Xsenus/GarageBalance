using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStableIncomeDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DestinationFundId",
                table: "income_types",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    destination_fund_id uuid;
                    changed_rows integer := 0;
                    affected_rows integer := 0;
                BEGIN
                    SELECT "Id"
                    INTO destination_fund_id
                    FROM funds
                    WHERE "NormalizedName" = 'ПРОЧЕЕ'
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT 1;

                    IF destination_fund_id IS NULL THEN
                        destination_fund_id := '58bc1538-5f77-46ce-9edf-8ed6aa73a701'::uuid;
                        INSERT INTO funds (
                            "Id", "Name", "NormalizedName", "Balance", "SortOrder", "AllowOperations",
                            "IsSystem", "CreatedAtUtc", "UpdatedAtUtc")
                        VALUES (
                            destination_fund_id, 'Прочее', 'ПРОЧЕЕ', 0, 70, FALSE,
                            TRUE, TIMESTAMPTZ '2026-07-18T16:06:21Z', TIMESTAMPTZ '2026-07-18T16:06:21Z');
                        changed_rows := changed_rows + 1;
                    END IF;

                    WITH defaults("Id", "Name", "Code") AS (
                        VALUES
                            ('4d253a1e-4e95-4a0e-a114-4b09122aa701'::uuid, 'Прочие оплаты', 'other_payments'),
                            ('81369836-864d-47e0-9d56-feb84d8aa702'::uuid, 'Прочие доходы', 'other_income')
                    )
                    UPDATE income_types existing
                    SET "Code" = defaults."Code",
                        "DestinationFundId" = destination_fund_id,
                        "IsSystem" = TRUE,
                        "IsArchived" = FALSE,
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-18T16:06:21Z'
                    FROM defaults
                    WHERE (existing."Id" = defaults."Id"
                           OR LOWER(BTRIM(existing."Name")) = LOWER(BTRIM(defaults."Name")))
                      AND NOT EXISTS (
                          SELECT 1
                          FROM income_types code_owner
                          WHERE code_owner."Id" <> existing."Id"
                            AND LOWER(BTRIM(code_owner."Code")) = LOWER(BTRIM(defaults."Code")));

                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    WITH defaults("Id", "Name", "Code") AS (
                        VALUES
                            ('4d253a1e-4e95-4a0e-a114-4b09122aa701'::uuid, 'Прочие оплаты', 'other_payments'),
                            ('81369836-864d-47e0-9d56-feb84d8aa702'::uuid, 'Прочие доходы', 'other_income')
                    )
                    INSERT INTO income_types (
                        "Id", "Name", "Code", "DestinationFundId", "IsSystem", "IsArchived",
                        "CreatedAtUtc", "UpdatedAtUtc")
                    SELECT
                        defaults."Id", defaults."Name", defaults."Code", destination_fund_id, TRUE, FALSE,
                        TIMESTAMPTZ '2026-07-18T16:06:21Z', TIMESTAMPTZ '2026-07-18T16:06:21Z'
                    FROM defaults
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM income_types existing
                        WHERE existing."Id" = defaults."Id"
                           OR LOWER(BTRIM(existing."Name")) = LOWER(BTRIM(defaults."Name"))
                           OR LOWER(BTRIM(existing."Code")) = LOWER(BTRIM(defaults."Code")))
                    ON CONFLICT DO NOTHING;

                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    UPDATE income_types
                    SET "DestinationFundId" = destination_fund_id,
                        "IsSystem" = TRUE,
                        "IsArchived" = FALSE,
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-18T16:06:21Z'
                    WHERE LOWER(BTRIM("Code")) IN ('other_payments', 'other_income')
                      AND ("DestinationFundId" IS DISTINCT FROM destination_fund_id
                           OR "IsSystem" = FALSE
                           OR "IsArchived" = TRUE);

                    GET DIAGNOSTICS affected_rows = ROW_COUNT;
                    changed_rows := changed_rows + affected_rows;

                    IF changed_rows > 0 THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            'a1fa26c7-2e4d-4790-9f3b-d3ff572aa703', TIMESTAMPTZ '2026-07-18T16:06:21Z',
                            'dictionary.income_destinations_initialized', 'dictionary', 'create',
                            'income_destination', 'system-defaults', 'Назначения поступлений',
                            'Добавлены стабильные системные назначения «Прочие оплаты» и «Прочие доходы» со связью с фондом «Прочее».',
                            jsonb_build_object(
                                'codes', jsonb_build_array('other_payments', 'other_income'),
                                'destinationFundId', destination_fund_id,
                                'changedRowCount', changed_rows)::text)
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_income_types_DestinationFundId",
                table: "income_types",
                column: "DestinationFundId");

            migrationBuilder.AddForeignKey(
                name: "FK_income_types_funds_DestinationFundId",
                table: "income_types",
                column: "DestinationFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM audit_events
                WHERE "Id" = 'a1fa26c7-2e4d-4790-9f3b-d3ff572aa703';

                DELETE FROM income_types
                WHERE "Id" IN (
                    '4d253a1e-4e95-4a0e-a114-4b09122aa701',
                    '81369836-864d-47e0-9d56-feb84d8aa702')
                  AND "CreatedAtUtc" = TIMESTAMPTZ '2026-07-18T16:06:21Z'
                  AND NOT EXISTS (
                      SELECT 1 FROM charge_service_settings
                      WHERE charge_service_settings."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM accruals
                      WHERE accruals."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM financial_operations
                      WHERE financial_operations."IncomeTypeId" = income_types."Id")
                  AND NOT EXISTS (
                      SELECT 1 FROM fee_campaigns
                      WHERE fee_campaigns."IncomeTypeId" = income_types."Id");

                UPDATE income_types
                SET "DestinationFundId" = NULL
                WHERE "DestinationFundId" = '58bc1538-5f77-46ce-9edf-8ed6aa73a701';

                DELETE FROM funds
                WHERE "Id" = '58bc1538-5f77-46ce-9edf-8ed6aa73a701'
                  AND "CreatedAtUtc" = TIMESTAMPTZ '2026-07-18T16:06:21Z'
                  AND NOT EXISTS (
                      SELECT 1 FROM fund_operations
                      WHERE fund_operations."FundId" = funds."Id");
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_income_types_funds_DestinationFundId",
                table: "income_types");

            migrationBuilder.DropIndex(
                name: "IX_income_types_DestinationFundId",
                table: "income_types");

            migrationBuilder.DropColumn(
                name: "DestinationFundId",
                table: "income_types");
        }
    }
}
