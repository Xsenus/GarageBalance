using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateServiceReportColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE application_settings SET "Key" = 'reports.service_columns.payments'
                WHERE "Key" = 'reports.service_columns';
                INSERT INTO application_settings ("Id", "Key", "BooleanValue", "JsonValue", "UpdatedAtUtc", "UpdatedByUserId", "Version")
                SELECT scope.id::uuid, scope.key, source."BooleanValue", source."JsonValue", source."UpdatedAtUtc", source."UpdatedByUserId", scope.id::uuid
                FROM application_settings source
                CROSS JOIN (VALUES
                    ('96f302bb-d6b0-4e91-aafe-b4713ff788e0', 'reports.service_columns.accrued'),
                    ('96f302bb-d6b0-4e91-aafe-b4713ff788e1', 'reports.service_columns.overdue')) AS scope(id, key)
                WHERE source."Key" = 'reports.service_columns.payments';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older application has one configuration; retain the payments configuration.
            migrationBuilder.Sql("""
                DELETE FROM application_settings WHERE "Key" IN ('reports.service_columns.accrued', 'reports.service_columns.overdue');
                UPDATE application_settings SET "Key" = 'reports.service_columns' WHERE "Key" = 'reports.service_columns.payments';
                """);
        }
    }
}
