using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260831014500_OptimizeMeterReadingYearGrid")]
public sealed class OptimizeMeterReadingYearGrid : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE meter_readings AS reading
            SET "IsMeterReplacement" = TRUE
            FROM meter_devices AS current_device
            WHERE reading."IsMeterReplacement" = FALSE
              AND reading."MeterDeviceId" = current_device."Id"
              AND date_trunc('month', current_device."InstalledOn")::date = reading."AccountingMonth"
              AND EXISTS (
                  SELECT 1
                  FROM meter_devices AS previous_device
                  WHERE previous_device."GarageId" = reading."GarageId"
                    AND previous_device."MeterKind" = reading."MeterKind"
                    AND previous_device."Id" <> current_device."Id"
                    AND previous_device."InstalledOn" < current_device."InstalledOn"
              );
            """);

        migrationBuilder.Sql(
            """
            CREATE INDEX "IX_garages_active_natural_number"
                ON garages ((length("Number")), "Number", "Id")
                WHERE "IsArchived" = false;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_garages_active_natural_number";
            """);
    }
}
