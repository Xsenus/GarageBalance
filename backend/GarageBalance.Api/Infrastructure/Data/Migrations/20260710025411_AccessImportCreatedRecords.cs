using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessImportCreatedRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_import_created_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceEntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceExternalId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    SourceRowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetEntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetEntityId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    TargetDisplayName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    RollbackStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RolledBackAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RolledBackByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RollbackReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_import_created_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_AccessImportRunId",
                table: "access_import_created_records",
                column: "AccessImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_AccessImportRunId_TargetEntit~",
                table: "access_import_created_records",
                columns: new[] { "AccessImportRunId", "TargetEntityType", "TargetEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_CreatedAtUtc",
                table: "access_import_created_records",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_RollbackStatus",
                table: "access_import_created_records",
                column: "RollbackStatus");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_SourceRowHash",
                table: "access_import_created_records",
                column: "SourceRowHash");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_created_records_SourceSystem_SourceEntityType",
                table: "access_import_created_records",
                columns: new[] { "SourceSystem", "SourceEntityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_import_created_records");
        }
    }
}
