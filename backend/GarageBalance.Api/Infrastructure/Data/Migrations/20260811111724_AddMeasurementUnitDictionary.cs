using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasurementUnitDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "measurement_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measurement_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_measurement_units_Name",
                table: "measurement_units",
                column: "Name",
                filter: "\"IsArchived\" = false");

            migrationBuilder.Sql(
                """
                WITH source_names AS (
                    SELECT unnest(ARRAY['руб.', 'руб./гараж', 'чел.', 'человек', 'м³', 'куб. м', 'кВт·ч']) AS name
                    UNION ALL
                    SELECT btrim("UnitName")
                    FROM charge_service_settings
                    WHERE "UnitName" IS NOT NULL AND btrim("UnitName") <> '' AND length(btrim("UnitName")) <= 40
                ), distinct_names AS (
                    SELECT min(name) AS name
                    FROM source_names
                    GROUP BY lower(name)
                )
                INSERT INTO measurement_units ("Id", "Name", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), name, false, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM distinct_names;

                CREATE UNIQUE INDEX "UX_measurement_units_ActiveName_CaseInsensitive"
                    ON measurement_units (lower("Name"))
                    WHERE "IsArchived" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "measurement_units");
        }
    }
}
