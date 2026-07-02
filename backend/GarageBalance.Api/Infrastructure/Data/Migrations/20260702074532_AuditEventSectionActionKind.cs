using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventSectionActionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionKind",
                table: "audit_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "audit_events",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE audit_events
                SET
                    "Section" = CASE
                        WHEN POSITION('.' IN "Action") > 0 THEN LEFT(SPLIT_PART("Action", '.', 1), 80)
                        ELSE 'system'
                    END,
                    "ActionKind" = CASE
                        WHEN LOWER("Action") LIKE '%\_created%' ESCAPE '\' THEN 'create'
                        WHEN LOWER("Action") LIKE '%\_updated%' ESCAPE '\' OR LOWER("Action") LIKE '%password_changed%' THEN 'update'
                        WHEN LOWER("Action") LIKE '%\_archived%' ESCAPE '\' THEN 'archive'
                        WHEN LOWER("Action") LIKE '%\_restored%' ESCAPE '\' THEN 'restore'
                        WHEN LOWER("Action") LIKE '%\_canceled%' ESCAPE '\' OR LOWER("Action") LIKE '%\_cancelled%' ESCAPE '\' THEN 'cancel'
                        WHEN LOWER("Action") LIKE '%\_deleted%' ESCAPE '\' THEN 'delete'
                        WHEN LOWER("Action") LIKE '%\_failed%' ESCAPE '\' OR LOWER("Action") LIKE '%\_rate_limited%' ESCAPE '\' OR LOWER("Action") LIKE '%\_inactive%' ESCAPE '\' THEN 'fail'
                        WHEN LOWER("Action") LIKE '%\_generated%' ESCAPE '\' THEN 'generate'
                        WHEN LOWER("Action") LIKE 'auth.login%' THEN 'login'
                        WHEN LOWER("Action") LIKE 'import.%' THEN 'import'
                        WHEN LOWER("Action") LIKE '%\_exported%' ESCAPE '\' OR LOWER("Action") LIKE '%.export%' THEN 'export'
                        ELSE 'other'
                    END
                WHERE "Section" IS NULL OR "ActionKind" IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActionKind",
                table: "audit_events",
                column: "ActionKind");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_Section",
                table: "audit_events",
                column: "Section");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_Section_ActionKind_CreatedAtUtc",
                table: "audit_events",
                columns: new[] { "Section", "ActionKind", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_ActionKind",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_Section",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_Section_ActionKind_CreatedAtUtc",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "ActionKind",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "audit_events");
        }
    }
}
