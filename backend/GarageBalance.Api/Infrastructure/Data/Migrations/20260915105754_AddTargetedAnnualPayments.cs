using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetedAnnualPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TargetAccrualId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_TargetAccrualId",
                table: "financial_operations",
                column: "TargetAccrualId");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_operations_accruals_TargetAccrualId",
                table: "financial_operations",
                column: "TargetAccrualId",
                principalTable: "accruals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_operations_accruals_TargetAccrualId",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_TargetAccrualId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "TargetAccrualId",
                table: "financial_operations");
        }
    }
}
