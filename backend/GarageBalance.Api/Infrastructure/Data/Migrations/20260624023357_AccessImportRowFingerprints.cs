using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessImportRowFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_import_row_fingerprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FingerprintKey = table.Column<string>(type: "character varying(520)", maxLength: 520, nullable: false),
                    SourceSystem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    RowHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccessImportRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetEntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TargetEntityId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_import_row_fingerprints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_import_row_fingerprints_AccessImportRunId",
                table: "access_import_row_fingerprints",
                column: "AccessImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_access_import_row_fingerprints_FingerprintKey",
                table: "access_import_row_fingerprints",
                column: "FingerprintKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_import_row_fingerprints_SourceSystem_EntityType",
                table: "access_import_row_fingerprints",
                columns: new[] { "SourceSystem", "EntityType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_import_row_fingerprints");
        }
    }
}
