using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkChargeServicesToExpenseTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseTypeId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    linked_service_count integer;
                BEGIN
                    WITH candidates AS (
                        SELECT
                            service."Id" AS service_id,
                            expense_type."Id" AS expense_type_id,
                            row_number() OVER (
                                PARTITION BY service."Id"
                                ORDER BY
                                    CASE
                                        WHEN income_type."Code" IS NOT NULL
                                          AND lower(btrim(expense_type."Code")) = CASE lower(btrim(income_type."Code"))
                                              WHEN 'water' THEN 'water_supply'
                                              WHEN 'trash' THEN 'trash_removal'
                                              ELSE lower(btrim(income_type."Code"))
                                          END
                                        THEN 0
                                        ELSE 1
                                    END,
                                    expense_type."CreatedAtUtc",
                                    expense_type."Id"
                            ) AS candidate_order
                        FROM charge_service_settings AS service
                        LEFT JOIN income_types AS income_type
                          ON income_type."Id" = service."IncomeTypeId"
                         AND NOT income_type."IsArchived"
                        INNER JOIN expense_types AS expense_type
                          ON NOT expense_type."IsArchived"
                         AND (
                              (
                                  income_type."Code" IS NOT NULL
                                  AND lower(btrim(expense_type."Code")) = CASE lower(btrim(income_type."Code"))
                                      WHEN 'water' THEN 'water_supply'
                                      WHEN 'trash' THEN 'trash_removal'
                                      ELSE lower(btrim(income_type."Code"))
                                  END
                              )
                              OR lower(btrim(expense_type."Name")) = lower(btrim(service."Name"))
                         )
                        WHERE service."ExpenseTypeId" IS NULL
                    )
                    UPDATE charge_service_settings AS service
                    SET "ExpenseTypeId" = candidate.expense_type_id,
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-23T11:40:59Z'
                    FROM candidates AS candidate
                    WHERE candidate.service_id = service."Id"
                      AND candidate.candidate_order = 1;

                    GET DIAGNOSTICS linked_service_count = ROW_COUNT;
                    IF linked_service_count > 0 THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType",
                            "EntityDisplayName", "Summary", "MetadataJson")
                        VALUES (
                            '4f09cf85-0a91-4af5-bf18-f0630e7bf47d',
                            TIMESTAMPTZ '2026-07-23T11:40:59Z',
                            'dictionary.charge_service_expense_types_linked',
                            'dictionary',
                            'update',
                            'charge_service',
                            'Услуги поставщиков',
                            'Услуги связаны с допустимыми видами начислений поставщикам.',
                            jsonb_build_object(
                                'linkedServiceCount', linked_service_count,
                                'reason', 'enforce-supplier-service-accrual-link')::text
                        )
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_ExpenseTypeId",
                table: "charge_service_settings",
                column: "ExpenseTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_expense_types_ExpenseTypeId",
                table: "charge_service_settings",
                column: "ExpenseTypeId",
                principalTable: "expense_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM audit_events
                WHERE "Id" = '4f09cf85-0a91-4af5-bf18-f0630e7bf47d';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_expense_types_ExpenseTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_ExpenseTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "ExpenseTypeId",
                table: "charge_service_settings");
        }
    }
}
