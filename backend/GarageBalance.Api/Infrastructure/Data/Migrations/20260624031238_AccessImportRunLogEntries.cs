using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessImportRunLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_import_run_log_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StepCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_import_run_log_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_run_log_entries_AccessImportRunId",
                table: "access_import_run_log_entries",
                column: "AccessImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_run_log_entries_AccessImportRunId_CreatedAtUtc",
                table: "access_import_run_log_entries",
                columns: new[] { "AccessImportRunId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_run_log_entries_CreatedAtUtc",
                table: "access_import_run_log_entries",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_import_run_log_entries");
        }
    }
}
