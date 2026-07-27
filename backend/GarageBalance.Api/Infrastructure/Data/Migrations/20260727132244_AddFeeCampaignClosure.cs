using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeCampaignClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                table: "fee_campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "fee_campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureComment",
                table: "fee_campaigns",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosedEarly",
                table: "fee_campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_fee_campaigns_ClosedAtUtc",
                table: "fee_campaigns",
                column: "ClosedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_fee_campaigns_ClosedAtUtc",
                table: "fee_campaigns");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "fee_campaigns");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "fee_campaigns");

            migrationBuilder.DropColumn(
                name: "ClosureComment",
                table: "fee_campaigns");

            migrationBuilder.DropColumn(
                name: "IsClosedEarly",
                table: "fee_campaigns");
        }
    }
}
