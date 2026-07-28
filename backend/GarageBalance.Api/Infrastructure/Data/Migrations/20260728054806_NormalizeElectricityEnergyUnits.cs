using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeElectricityEnergyUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql(
                """
                UPDATE tariffs
                SET "ElectricityFirstTierName" = regexp_replace("ElectricityFirstTierName", 'кВт(?!·ч)', 'кВт·ч', 'g'),
                    "ElectricitySecondTierName" = regexp_replace("ElectricitySecondTierName", 'кВт(?!·ч)', 'кВт·ч', 'g'),
                    "ElectricityThirdTierName" = regexp_replace("ElectricityThirdTierName", 'кВт(?!·ч)', 'кВт·ч', 'g'),
                    "ElectricityTiersJson" = CASE
                        WHEN "ElectricityTiersJson" IS NULL THEN NULL
                        ELSE regexp_replace("ElectricityTiersJson"::text, 'кВт(?!·ч)', 'кВт·ч', 'g')::jsonb
                    END
                WHERE "CalculationBase" = 'meter_electricity'
                  AND (
                      COALESCE("ElectricityFirstTierName", '') ~ 'кВт(?!·ч)'
                      OR COALESCE("ElectricitySecondTierName", '') ~ 'кВт(?!·ч)'
                      OR COALESCE("ElectricityThirdTierName", '') ~ 'кВт(?!·ч)'
                      OR COALESCE("ElectricityTiersJson"::text, '') ~ 'кВт(?!·ч)');

                UPDATE charge_service_settings AS setting
                SET "UnitName" = 'кВт·ч'
                FROM tariffs AS tariff
                WHERE setting."TariffId" = tariff."Id"
                  AND tariff."CalculationBase" = 'meter_electricity'
                  AND BTRIM(setting."UnitName") = 'кВт';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The physical-unit correction is intentionally irreversible: reverting would make energy values incorrect again.
        }
    }
}
