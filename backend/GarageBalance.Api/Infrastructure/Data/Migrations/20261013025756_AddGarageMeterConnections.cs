using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageMeterConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "garage_meter_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterKind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ConnectedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineMeterDeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garage_meter_connections", x => x.Id);
                    table.CheckConstraint("CK_garage_meter_connections_period", "\"ClosedOn\" IS NULL OR \"ClosedOn\" >= \"ConnectedOn\"");
                    table.ForeignKey(
                        name: "FK_garage_meter_connections_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_garage_meter_connections_meter_devices_BaselineMeterDeviceId",
                        column: x => x.BaselineMeterDeviceId,
                        principalTable: "meter_devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_garage_meter_connections_BaselineMeterDeviceId",
                table: "garage_meter_connections",
                column: "BaselineMeterDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_garage_meter_connections_GarageId_MeterKind",
                table: "garage_meter_connections",
                columns: new[] { "GarageId", "MeterKind" },
                unique: true,
                filter: "\"ClosedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_garage_meter_connections_GarageId_MeterKind_ConnectedOn",
                table: "garage_meter_connections",
                columns: new[] { "GarageId", "MeterKind", "ConnectedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "garage_meter_connections");
        }
    }
}
