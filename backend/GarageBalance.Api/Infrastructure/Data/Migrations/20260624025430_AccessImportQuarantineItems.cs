using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessImportQuarantineItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_import_quarantine_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessImportRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    RowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReasonMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RowSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionComment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_import_quarantine_items", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_AccessImportRunId",
                table: "access_import_quarantine_items",
                column: "AccessImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_CreatedAtUtc",
                table: "access_import_quarantine_items",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_RowHash",
                table: "access_import_quarantine_items",
                column: "RowHash");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_SourceSystem_EntityType",
                table: "access_import_quarantine_items",
                columns: new[] { "SourceSystem", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_quarantine_items_Status",
                table: "access_import_quarantine_items",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_import_quarantine_items");
        }
    }
}
