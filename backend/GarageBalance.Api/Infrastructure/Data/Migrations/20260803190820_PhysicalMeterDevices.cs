using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PhysicalMeterDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MeterDeviceId",
                table: "meter_readings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreviousDeviceConsumption",
                table: "meter_readings",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsMeterReplacement",
                table: "meter_readings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "meter_devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InstalledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RemovedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    InitialValue = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    FinalValue = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meter_devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meter_devices_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                WITH first_readings AS (
                    SELECT DISTINCT ON (reading."GarageId", reading."MeterKind")
                        reading."GarageId",
                        reading."MeterKind",
                        reading."ReadingDate",
                        reading."PreviousValue"
                    FROM meter_readings AS reading
                    ORDER BY reading."GarageId", reading."MeterKind", reading."AccountingMonth", reading."ReadingDate", reading."Id"
                )
                INSERT INTO meter_devices (
                    "Id", "GarageId", "MeterKind", "SerialNumber", "InstalledOn", "RemovedOn",
                    "InitialValue", "FinalValue", "Version", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    md5(first_reading."GarageId"::text || ':' || first_reading."MeterKind")::uuid,
                    first_reading."GarageId",
                    first_reading."MeterKind",
                    'Без номера',
                    first_reading."ReadingDate",
                    NULL,
                    first_reading."PreviousValue",
                    NULL,
                    md5('version:' || first_reading."GarageId"::text || ':' || first_reading."MeterKind")::uuid,
                    NOW(),
                    NOW()
                FROM first_readings AS first_reading;

                UPDATE meter_readings AS reading
                SET "MeterDeviceId" = md5(reading."GarageId"::text || ':' || reading."MeterKind")::uuid
                WHERE reading."MeterDeviceId" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_MeterDeviceId",
                table: "meter_readings",
                column: "MeterDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_GarageId_MeterKind",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind" },
                unique: true,
                filter: "\"RemovedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_GarageId_MeterKind_InstalledOn",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind", "InstalledOn" });

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_GarageId_MeterKind_SerialNumber",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind", "SerialNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_meter_readings_meter_devices_MeterDeviceId",
                table: "meter_readings",
                column: "MeterDeviceId",
                principalTable: "meter_devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_meter_readings_meter_devices_MeterDeviceId",
                table: "meter_readings");

            migrationBuilder.DropTable(
                name: "meter_devices");

            migrationBuilder.DropIndex(
                name: "IX_meter_readings_MeterDeviceId",
                table: "meter_readings");

            migrationBuilder.DropColumn(
                name: "MeterDeviceId",
                table: "meter_readings");

            migrationBuilder.DropColumn(
                name: "PreviousDeviceConsumption",
                table: "meter_readings");

            migrationBuilder.DropColumn(
                name: "IsMeterReplacement",
                table: "meter_readings");
        }
    }
}
