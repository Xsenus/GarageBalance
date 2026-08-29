using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeAuditFilterCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE audit_events
                SET "Section" = LOWER("Section")
                WHERE "Section" IS NOT NULL
                  AND "Section" <> LOWER("Section");

                UPDATE audit_events
                SET "ActionKind" = LOWER("ActionKind")
                WHERE "ActionKind" IS NOT NULL
                  AND "ActionKind" <> LOWER("ActionKind");

                ALTER TABLE audit_events
                ADD CONSTRAINT "CK_audit_events_Section_lowercase"
                CHECK ("Section" IS NULL OR "Section" = LOWER("Section"));

                ALTER TABLE audit_events
                ADD CONSTRAINT "CK_audit_events_ActionKind_lowercase"
                CHECK ("ActionKind" IS NULL OR "ActionKind" = LOWER("ActionKind"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE audit_events
                DROP CONSTRAINT IF EXISTS "CK_audit_events_ActionKind_lowercase";

                ALTER TABLE audit_events
                DROP CONSTRAINT IF EXISTS "CK_audit_events_Section_lowercase";
                """);
        }
    }
}
