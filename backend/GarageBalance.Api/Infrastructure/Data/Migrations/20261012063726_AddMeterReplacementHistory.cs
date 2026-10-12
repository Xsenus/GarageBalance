using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeterReplacementHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_meter_devices_GarageId_MeterKind_SerialNumber", table: "meter_devices");
            migrationBuilder.DropIndex(
                name: "IX_meter_devices_GarageId_MeterKind",
                table: "meter_devices");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CanceledAtUtc",
                table: "meter_devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "meter_devices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasReplacementSnapshot",
                table: "meter_devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanceled",
                table: "meter_devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreviousReadingSnapshotJson",
                table: "meter_devices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedDeviceId",
                table: "meter_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReplacementAccountingMonth",
                table: "meter_devices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementReadingId",
                table: "meter_devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplacementReason",
                table: "meter_devices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_GarageId_MeterKind",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind" },
                unique: true,
                filter: "\"RemovedOn\" IS NULL AND \"IsCanceled\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_ReplacedDeviceId",
                table: "meter_devices",
                column: "ReplacedDeviceId");
            migrationBuilder.CreateIndex(name: "IX_meter_devices_GarageId_MeterKind_SerialNumber", table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind", "SerialNumber" }, unique: true, filter: "\"IsCanceled\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_meter_devices_meter_devices_ReplacedDeviceId",
                table: "meter_devices",
                column: "ReplacedDeviceId",
                principalTable: "meter_devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Link old replacements without inventing a missing pre-replacement reading.
            migrationBuilder.Sql("""
                WITH legacy_replacements AS (
                    SELECT DISTINCT ON (successor."Id") successor."Id", predecessor."Id" AS predecessor_id,
                        reading."Id" AS reading_id, reading."AccountingMonth", reading."Comment"
                    FROM meter_devices successor
                    JOIN meter_devices predecessor ON predecessor."GarageId" = successor."GarageId"
                        AND predecessor."MeterKind" = successor."MeterKind"
                        AND predecessor."InstalledOn" < successor."InstalledOn"
                        AND predecessor."RemovedOn" IN (successor."InstalledOn" - 1, successor."InstalledOn")
                        AND predecessor."FinalValue" IS NOT NULL
                    JOIN meter_readings reading ON reading."GarageId" = successor."GarageId"
                        AND reading."MeterKind" = successor."MeterKind" AND NOT reading."IsCanceled"
                        AND reading."IsMeterReplacement"
                        AND reading."AccountingMonth" = date_trunc('month', successor."InstalledOn")::date
                    WHERE successor."SerialNumber" NOT LIKE 'Начало учёта%'
                    ORDER BY successor."Id", predecessor."InstalledOn" DESC, predecessor."Id"
                )
                UPDATE meter_devices device SET "ReplacedDeviceId" = legacy.predecessor_id,
                    "ReplacementAccountingMonth" = legacy."AccountingMonth", "ReplacementReadingId" = legacy.reading_id,
                    "ReplacementReason" = left(legacy."Comment", 500)
                FROM legacy_replacements legacy WHERE device."Id" = legacy."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_meter_devices_GarageId_MeterKind_SerialNumber", table: "meter_devices");
            // Keep canceled physical devices inactive in the old schema as well.
            migrationBuilder.Sql("""
                UPDATE meter_devices SET "RemovedOn" = COALESCE("RemovedOn", "InstalledOn"),
                    "FinalValue" = COALESCE("FinalValue", "InitialValue"),
                    "SerialNumber" = left("SerialNumber", 50) || ' [отмена ' || "Id"::text || ']'
                WHERE "IsCanceled";
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_meter_devices_meter_devices_ReplacedDeviceId",
                table: "meter_devices");

            migrationBuilder.DropIndex(
                name: "IX_meter_devices_GarageId_MeterKind",
                table: "meter_devices");

            migrationBuilder.DropIndex(
                name: "IX_meter_devices_ReplacedDeviceId",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "CanceledAtUtc",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "HasReplacementSnapshot",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "IsCanceled",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "PreviousReadingSnapshotJson",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "ReplacedDeviceId",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "ReplacementAccountingMonth",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "ReplacementReadingId",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "ReplacementReason",
                table: "meter_devices");

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_GarageId_MeterKind",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind" },
                unique: true,
                filter: "\"RemovedOn\" IS NULL");
            migrationBuilder.CreateIndex(name: "IX_meter_devices_GarageId_MeterKind_SerialNumber", table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind", "SerialNumber" }, unique: true);
        }
    }
}
