using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RouteFeeCampaignAccrualsToOtherIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.AddColumn<Guid>(
                name: "FeeCampaignId",
                table: "accruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE accruals AS accrual
                SET "FeeCampaignId" = (
                    SELECT campaign."Id"
                    FROM fee_campaigns AS campaign
                    WHERE accrual."Comment" IS NOT NULL
                      AND STRPOS(LOWER(accrual."Comment"), LOWER(campaign."Name" || ':')) > 0
                      AND accrual."AccountingMonth" >= DATE_TRUNC('month', campaign."StartsOn")::date
                      AND (campaign."EndsOn" IS NULL OR accrual."AccountingMonth" <= DATE_TRUNC('month', campaign."EndsOn")::date)
                    ORDER BY campaign."Id"
                    LIMIT 1)
                WHERE accrual."Source" = 'fee_campaign'
                  AND accrual."FeeCampaignId" IS NULL
                  AND 1 = (
                      SELECT COUNT(*)
                      FROM fee_campaigns AS campaign
                      WHERE accrual."Comment" IS NOT NULL
                        AND STRPOS(LOWER(accrual."Comment"), LOWER(campaign."Name" || ':')) > 0
                        AND accrual."AccountingMonth" >= DATE_TRUNC('month', campaign."StartsOn")::date
                        AND (campaign."EndsOn" IS NULL OR accrual."AccountingMonth" <= DATE_TRUNC('month', campaign."EndsOn")::date));

                DO $$
                DECLARE
                    destination_id uuid;
                    changed_campaigns integer := 0;
                    linked_accruals integer := 0;
                BEGIN
                    SELECT "Id"
                    INTO destination_id
                    FROM income_types
                    WHERE LOWER(BTRIM("Code")) = 'other_income'
                      AND "IsSystem" = TRUE
                      AND "IsArchived" = FALSE
                      AND "DestinationFundId" IS NOT NULL
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT 1;

                    IF destination_id IS NULL THEN
                        RAISE EXCEPTION 'System income destination other_income is not configured';
                    END IF;

                    UPDATE fee_campaigns
                    SET "IncomeTypeId" = destination_id,
                        "UpdatedAtUtc" = TIMESTAMPTZ '2026-07-18T17:25:21Z'
                    WHERE "IncomeTypeId" <> destination_id;
                    GET DIAGNOSTICS changed_campaigns = ROW_COUNT;

                    SELECT COUNT(*) INTO linked_accruals
                    FROM accruals
                    WHERE "FeeCampaignId" IS NOT NULL;

                    INSERT INTO audit_events (
                        "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "EntityId",
                        "EntityDisplayName", "Summary", "MetadataJson")
                    VALUES (
                        'ce8f1c2a-615e-4d65-a01d-a4b83a4ac704', TIMESTAMPTZ '2026-07-18T17:25:21Z',
                        'finance.fee_campaign_routing_initialized', 'finance', 'update',
                        'fee_campaign', 'system-routing', 'Маршрутизация объявленных сборов',
                        'Объявленные сборы направлены в системное назначение «Прочие доходы», исторические начисления связаны с однозначно определенными сборами.',
                        jsonb_build_object(
                            'incomeTypeCode', 'other_income',
                            'incomeTypeId', destination_id,
                            'changedCampaignCount', changed_campaigns,
                            'linkedAccrualCount', linked_accruals)::text)
                    ON CONFLICT ("Id") DO NOTHING;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_FeeCampaignId",
                table: "accruals",
                column: "FeeCampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_FeeCampaignId_AccountingMonth",
                table: "accruals",
                columns: new[] { "GarageId", "FeeCampaignId", "AccountingMonth" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"FeeCampaignId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL AND \"FeeCampaignId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_accruals_fee_campaigns_FeeCampaignId",
                table: "accruals",
                column: "FeeCampaignId",
                principalTable: "fee_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM audit_events
                WHERE "Id" = 'ce8f1c2a-615e-4d65-a01d-a4b83a4ac704';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_accruals_fee_campaigns_FeeCampaignId",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_FeeCampaignId",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_FeeCampaignId_AccountingMonth",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "FeeCampaignId",
                table: "accruals");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true,
                filter: "\"IsCanceled\" = false AND \"IrregularPaymentId\" IS NULL");
        }
    }
}
