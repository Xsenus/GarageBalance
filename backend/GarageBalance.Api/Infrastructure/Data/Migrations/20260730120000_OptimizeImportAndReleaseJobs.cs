using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeImportAndReleaseJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_access_import_runs_ContentSha256_StartedAtUtc",
                table: "access_import_runs",
                columns: new[] { "ContentSha256", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_runs_Status_StartedAtUtc",
                table: "access_import_runs",
                columns: new[] { "Status", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_Status_AccessImportRunId_Cre~",
                table: "access_import_quarantine_items",
                columns: new[] { "Status", "AccessImportRunId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_Status_CreatedAtUtc_Id",
                table: "access_import_quarantine_items",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_access_import_runs_ContentSha256_StartedAtUtc",
                table: "access_import_runs");

            migrationBuilder.DropIndex(
                name: "IX_access_import_runs_Status_StartedAtUtc",
                table: "access_import_runs");

            migrationBuilder.DropIndex(
                name: "IX_access_import_quarantine_items_Status_AccessImportRunId_Cre~",
                table: "access_import_quarantine_items");

            migrationBuilder.DropIndex(
                name: "IX_access_import_quarantine_items_Status_CreatedAtUtc_Id",
                table: "access_import_quarantine_items");
        }
    }
}
