using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargeServiceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charge_service_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRegular = table.Column<bool>(type: "boolean", nullable: false),
                    PeriodicityMonths = table.Column<int>(type: "integer", nullable: true),
                    AccrualStartMonth = table.Column<int>(type: "integer", nullable: true),
                    PaymentDueDay = table.Column<int>(type: "integer", nullable: true),
                    PaymentDueMonth = table.Column<int>(type: "integer", nullable: true),
                    OverdueGraceDays = table.Column<int>(type: "integer", nullable: false),
                    IsMetered = table.Column<bool>(type: "boolean", nullable: false),
                    HasTieredTariff = table.Column<bool>(type: "boolean", nullable: false),
                    UnitName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charge_service_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_HasTieredTariff",
                table: "charge_service_settings",
                column: "HasTieredTariff");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_IsMetered",
                table: "charge_service_settings",
                column: "IsMetered");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_IsRegular",
                table: "charge_service_settings",
                column: "IsRegular");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_Name",
                table: "charge_service_settings",
                column: "Name",
                unique: true,
                filter: "\"IsArchived\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charge_service_settings");
        }
    }
}
