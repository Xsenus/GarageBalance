using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChargeServiceAccountingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IncomeTypeId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TariffId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_IncomeTypeId",
                table: "charge_service_settings",
                column: "IncomeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_TariffId",
                table: "charge_service_settings",
                column: "TariffId");

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_income_types_IncomeTypeId",
                table: "charge_service_settings",
                column: "IncomeTypeId",
                principalTable: "income_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_tariffs_TariffId",
                table: "charge_service_settings",
                column: "TariffId",
                principalTable: "tariffs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_income_types_IncomeTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_tariffs_TariffId",
                table: "charge_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_IncomeTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_TariffId",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "IncomeTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "TariffId",
                table: "charge_service_settings");
        }
    }
}
