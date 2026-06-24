using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccrualTariffReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TariffId",
                table: "accruals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_TariffId",
                table: "accruals",
                column: "TariffId");

            migrationBuilder.AddForeignKey(
                name: "FK_accruals_tariffs_TariffId",
                table: "accruals",
                column: "TariffId",
                principalTable: "tariffs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_accruals_tariffs_TariffId",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_TariffId",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "TariffId",
                table: "accruals");
        }
    }
}
