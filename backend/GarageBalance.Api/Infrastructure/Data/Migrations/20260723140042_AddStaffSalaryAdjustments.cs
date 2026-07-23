using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffSalaryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_salary_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    AdjustmentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_salary_adjustments", x => x.Id);
                    table.CheckConstraint("CK_staff_salary_adjustments_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_staff_salary_adjustments_AdjustmentType", "\"AdjustmentType\" IN ('bonus', 'penalty')");
                    table.ForeignKey(
                        name: "FK_staff_salary_adjustments_staff_members_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "staff_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_staff_salary_adjustments_AccountingMonth",
                table: "staff_salary_adjustments",
                column: "AccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_staff_salary_adjustments_StaffMemberId",
                table: "staff_salary_adjustments",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_staff_salary_adjustments_StaffMemberId_AccountingMonth_Adju~",
                table: "staff_salary_adjustments",
                columns: new[] { "StaffMemberId", "AccountingMonth", "AdjustmentType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_salary_adjustments");
        }
    }
}
