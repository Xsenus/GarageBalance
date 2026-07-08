using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FundOperationCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCanceled",
                table: "fund_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "fund_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fund_operations_IsCanceled",
                table: "fund_operations",
                column: "IsCanceled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fund_operations_IsCanceled",
                table: "fund_operations");

            migrationBuilder.DropColumn(
                name: "IsCanceled",
                table: "fund_operations");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "fund_operations");
        }
    }
}
