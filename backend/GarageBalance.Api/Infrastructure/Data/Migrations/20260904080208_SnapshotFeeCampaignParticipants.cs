using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotFeeCampaignParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO fee_campaign_garages ("FeeCampaignId", "GarageId")
                SELECT campaign."Id", garage."Id"
                FROM fee_campaigns AS campaign
                CROSS JOIN garages AS garage
                WHERE campaign."AppliesToAllGarages" = TRUE
                  AND (
                    (garage."IsArchived" = FALSE AND garage."CreatedAtUtc" <= campaign."CreatedAtUtc")
                    OR EXISTS (
                      SELECT 1
                      FROM accruals AS accrual
                      WHERE accrual."FeeCampaignId" = campaign."Id"
                        AND accrual."GarageId" = garage."Id"))
                ON CONFLICT ("FeeCampaignId", "GarageId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The participant snapshot is business history and must not be removed on downgrade.
        }
    }
}
