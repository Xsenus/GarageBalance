using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceReportColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JsonValue",
                table: "application_settings",
                type: "jsonb",
                nullable: true);
            migrationBuilder.Sql("""
                INSERT INTO application_settings ("Id", "Key", "BooleanValue", "UpdatedAtUtc", "Version")
                VALUES ('96f302bb-d6b0-4e91-aafe-b4713ff788df', 'reports.service_columns', false, CURRENT_TIMESTAMP, '96f302bb-d6b0-4e91-aafe-b4713ff788df')
                ON CONFLICT ("Key") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM application_settings WHERE \"Key\" = 'reports.service_columns';");
            migrationBuilder.DropColumn(
                name: "JsonValue",
                table: "application_settings");
        }
    }
}
