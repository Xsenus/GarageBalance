using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinancialOperationStaffMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_StaffMemberId",
                table: "financial_operations",
                column: "StaffMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_financial_operations_staff_members_StaffMemberId",
                table: "financial_operations",
                column: "StaffMemberId",
                principalTable: "staff_members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_financial_operations_staff_members_StaffMemberId",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_StaffMemberId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "financial_operations");
        }
    }
}
