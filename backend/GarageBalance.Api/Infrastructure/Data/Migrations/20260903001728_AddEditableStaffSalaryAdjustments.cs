using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEditableStaffSalaryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CanceledAtUtc",
                table: "staff_salary_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "staff_salary_adjustments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanceled",
                table: "staff_salary_adjustments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "staff_salary_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanceledAtUtc",
                table: "staff_salary_adjustments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "staff_salary_adjustments");

            migrationBuilder.DropColumn(
                name: "IsCanceled",
                table: "staff_salary_adjustments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "staff_salary_adjustments");
        }
    }
}
