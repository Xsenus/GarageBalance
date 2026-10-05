using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreLegacyAdministratorOpeningDataPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the unchanged legacy system grant set is repaired. An explicitly edited
            // administrator role is a security decision and must remain untouched.
            migrationBuilder.Sql("""
                WITH legacy AS MATERIALIZED (
                    SELECT r."Id", r."Code", r."Name", r."Permissions"::jsonb AS old_permissions
                    FROM app_roles r
                    WHERE r."Code" = 'administrator'
                      AND jsonb_array_length(r."Permissions"::jsonb) = 11
                      AND r."Permissions"::jsonb @> '["app_releases.manage","audit.read","dictionaries.read","dictionaries.write","import.run","payments.meter_readings.historical_correct","payments.read","payments.write","reports.read","tariffs.manage","users.manage"]'::jsonb
                      AND NOT EXISTS (
                          SELECT 1 FROM audit_events a
                          WHERE a."Action" = 'users.role_permissions_updated'
                            AND a."EntityType" = 'app_role' AND a."EntityId" = r."Code")
                    FOR UPDATE OF r
                ), repaired AS (
                    UPDATE app_roles r
                    SET "Permissions" = (
                        SELECT jsonb_agg(permission ORDER BY permission)
                        FROM jsonb_array_elements_text(legacy.old_permissions || '["opening_data.adjust"]'::jsonb) permission)::text,
                        "Version" = gen_random_uuid()
                    FROM legacy WHERE r."Id" = legacy."Id"
                    RETURNING r."Id", r."Code", r."Name", legacy.old_permissions, r."Permissions"
                ), invalidated AS (
                    UPDATE app_users u
                    SET "SessionVersion" = u."SessionVersion" + 1, "Version" = gen_random_uuid()
                    WHERE EXISTS (
                        SELECT 1 FROM app_user_roles ur JOIN repaired r ON r."Id" = ur."RoleId"
                        WHERE ur."UserId" = u."Id")
                    RETURNING u."Id"
                )
                INSERT INTO audit_events (
                    "Id", "CreatedAtUtc", "Action", "EntityType", "EntityId", "EntityDisplayName",
                    "Section", "ActionKind", "Summary", "MetadataJson")
                SELECT gen_random_uuid(), now(), 'users.legacy_opening_permission_restored',
                    'app_role', r."Code", r."Name", 'users', 'update',
                    'Восстановлено право корректировки начальных данных у прежней системной роли администратора.',
                    jsonb_build_object(
                        'migration', '20261005085727_RestoreLegacyAdministratorOpeningDataPermission',
                        'oldValues', jsonb_build_object('permissions', r.old_permissions::text),
                        'newValues', jsonb_build_object('permissions', r."Permissions"::text),
                        'fieldLabels', jsonb_build_object('permissions', 'Права'),
                        'invalidatedSessions', (SELECT count(*) FROM invalidated))::text
                FROM repaired r;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retain the corrected access on rollback; later administrator choices must
            // never be overwritten by reversing this one-time data repair.
        }
    }
}
