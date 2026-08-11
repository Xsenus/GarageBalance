using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkFeeCampaignPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FeeCampaignId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_FeeCampaignId",
                table: "financial_operations",
                column: "FeeCampaignId");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_operations_fee_campaigns_FeeCampaignId",
                table: "financial_operations",
                column: "FeeCampaignId",
                principalTable: "fee_campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE accruals AS a
                SET "Basis" = f."Name"
                FROM fee_campaigns AS f
                WHERE a."FeeCampaignId" = f."Id"
                  AND (a."Basis" IS NULL OR btrim(a."Basis") = '');

                UPDATE financial_operations AS operation
                SET "FeeCampaignId" = matched."FeeCampaignId"
                FROM (
                    SELECT allocation."FinancialOperationId",
                           (array_agg(DISTINCT accrual."FeeCampaignId"))[1] AS "FeeCampaignId"
                    FROM accrual_payment_allocations AS allocation
                    JOIN accruals AS accrual ON accrual."Id" = allocation."AccrualId"
                    WHERE allocation."IsActive" = TRUE
                    GROUP BY allocation."FinancialOperationId"
                    HAVING count(DISTINCT accrual."FeeCampaignId") = 1
                       AND count(*) FILTER (WHERE accrual."FeeCampaignId" IS NULL) = 0
                ) AS matched
                WHERE operation."Id" = matched."FinancialOperationId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_operations_fee_campaigns_FeeCampaignId",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_FeeCampaignId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "FeeCampaignId",
                table: "financial_operations");
        }
    }
}
