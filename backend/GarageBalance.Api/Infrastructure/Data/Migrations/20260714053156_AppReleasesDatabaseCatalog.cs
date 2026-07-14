using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppReleasesDatabaseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_releases",
                columns: table => new
                {
                    ReleaseId = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_releases", x => x.ReleaseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_releases_IsPublished_PublishedAt",
                table: "app_releases",
                columns: new[] { "IsPublished", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_app_releases_Version",
                table: "app_releases",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_releases");
        }
    }
}
