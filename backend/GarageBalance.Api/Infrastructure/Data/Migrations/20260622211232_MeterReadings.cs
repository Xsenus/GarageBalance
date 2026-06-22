using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MeterReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meter_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AccountingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentValue = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    PreviousValue = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Consumption = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsCanceled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meter_readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meter_readings_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_AccountingMonth",
                table: "meter_readings",
                column: "AccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_GarageId",
                table: "meter_readings",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_GarageId_MeterKind_AccountingMonth",
                table: "meter_readings",
                columns: new[] { "GarageId", "MeterKind", "AccountingMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_ReadingDate",
                table: "meter_readings",
                column: "ReadingDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meter_readings");
        }
    }
}
