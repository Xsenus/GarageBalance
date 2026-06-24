using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationSecretSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_secret_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SettingKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NormalizedProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedSettingKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ProtectedValue = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_secret_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_secret_settings_Provider",
                table: "integration_secret_settings",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_integration_secret_settings_provider_key",
                table: "integration_secret_settings",
                columns: new[] { "NormalizedProvider", "NormalizedSettingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_secret_settings_UpdatedAtUtc",
                table: "integration_secret_settings",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_secret_settings");
        }
    }
}
