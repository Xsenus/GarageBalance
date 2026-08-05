using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargeServiceTariffHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charge_service_tariff_versions",
                columns: table => new
                {
                    ChargeServiceSettingId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charge_service_tariff_versions", x => new { x.ChargeServiceSettingId, x.EffectiveFrom });
                    table.ForeignKey(
                        name: "FK_charge_service_tariff_versions_charge_service_settings_Char~",
                        column: x => x.ChargeServiceSettingId,
                        principalTable: "charge_service_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_charge_service_tariff_versions_tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_tariff_versions_TariffId",
                table: "charge_service_tariff_versions",
                column: "TariffId");

            migrationBuilder.Sql(
                """
                INSERT INTO charge_service_tariff_versions
                    ("ChargeServiceSettingId", "EffectiveFrom", "TariffId", "CreatedAtUtc")
                SELECT service."Id", tariff."EffectiveFrom", tariff."Id", NOW()
                FROM charge_service_settings AS service
                INNER JOIN tariffs AS tariff ON tariff."Id" = service."TariffId"
                WHERE service."TariffId" IS NOT NULL
                ON CONFLICT ("ChargeServiceSettingId", "EffectiveFrom") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charge_service_tariff_versions");
        }
    }
}
