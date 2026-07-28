using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageReportQuickLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "garage_report_quick_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    ArchivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garage_report_quick_lists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "garage_report_quick_list_garages",
                columns: table => new
                {
                    QuickListId = table.Column<Guid>(type: "uuid", nullable: false),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garage_report_quick_list_garages", x => new { x.QuickListId, x.GarageId });
                    table.ForeignKey(
                        name: "FK_garage_report_quick_list_garages_garage_report_quick_lists_~",
                        column: x => x.QuickListId,
                        principalTable: "garage_report_quick_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_garage_report_quick_list_garages_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_garage_report_quick_list_garages_GarageId",
                table: "garage_report_quick_list_garages",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_garage_report_quick_lists_NormalizedName",
                table: "garage_report_quick_lists",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsArchived\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_garage_report_quick_lists_UpdatedAtUtc",
                table: "garage_report_quick_lists",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "garage_report_quick_list_garages");

            migrationBuilder.DropTable(
                name: "garage_report_quick_lists");
        }
    }
}
