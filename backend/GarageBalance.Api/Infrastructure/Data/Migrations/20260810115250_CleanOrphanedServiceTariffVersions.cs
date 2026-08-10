using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CleanOrphanedServiceTariffVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM tariffs AS tariff
                WHERE tariff."IsTemplate" = FALSE
                  AND NOT EXISTS (
                    SELECT 1
                    FROM charge_service_settings AS setting
                    WHERE setting."TariffId" = tariff."Id")
                  AND NOT EXISTS (
                    SELECT 1
                    FROM charge_service_tariff_versions AS version
                    WHERE version."TariffId" = tariff."Id")
                  AND NOT EXISTS (
                    SELECT 1
                    FROM accruals AS accrual
                    WHERE accrual."TariffId" = tariff."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unreferenced generated versions cannot be restored without reintroducing obsolete data.
        }
    }
}
