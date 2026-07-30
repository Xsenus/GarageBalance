using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeUsersAndAuditSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "audit_events",
                type: "text",
                nullable: false,
                computedColumnSql: "lower(\n    coalesce(\"Action\", '') || ' ' ||\n    coalesce(\"EntityType\", '') || ' ' ||\n    coalesce(\"EntityId\", '') || ' ' ||\n    coalesce(\"EntityDisplayName\", '') || ' ' ||\n    coalesce(\"RelatedGarageId\", '') || ' ' ||\n    coalesce(\"RelatedGarageNumber\", '') || ' ' ||\n    coalesce(\"RelatedAccountingMonth\", '') || ' ' ||\n    coalesce(\"RelatedCounterpartyId\", '') || ' ' ||\n    coalesce(\"RelatedCounterpartyName\", '') || ' ' ||\n    coalesce(\"RelatedDocumentId\", '') || ' ' ||\n    coalesce(\"RelatedDocumentNumber\", '') || ' ' ||\n    coalesce(\"Summary\", '')\n)",
                stored: true);

            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_app_users_NormalizedEmail_trgm" ON "app_users" USING gin ("NormalizedEmail" gin_trgm_ops);""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_app_users_DisplayName_trgm" ON "app_users" USING gin ("DisplayName" gin_trgm_ops);""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_SearchText_trgm" ON "audit_events" USING gin ("SearchText" gin_trgm_ops);""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedGarageId_trgm" ON "audit_events" USING gin ("RelatedGarageId" gin_trgm_ops) WHERE "RelatedGarageId" IS NOT NULL;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedGarageNumber_trgm" ON "audit_events" USING gin ("RelatedGarageNumber" gin_trgm_ops) WHERE "RelatedGarageNumber" IS NOT NULL;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedCounterpartyId_trgm" ON "audit_events" USING gin ("RelatedCounterpartyId" gin_trgm_ops) WHERE "RelatedCounterpartyId" IS NOT NULL;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedCounterpartyName_trgm" ON "audit_events" USING gin ("RelatedCounterpartyName" gin_trgm_ops) WHERE "RelatedCounterpartyName" IS NOT NULL;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedDocumentId_trgm" ON "audit_events" USING gin ("RelatedDocumentId" gin_trgm_ops) WHERE "RelatedDocumentId" IS NOT NULL;""");
            migrationBuilder.Sql(
                """CREATE INDEX "IX_audit_events_RelatedDocumentNumber_trgm" ON "audit_events" USING gin ("RelatedDocumentNumber" gin_trgm_ops) WHERE "RelatedDocumentNumber" IS NOT NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedDocumentNumber_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedDocumentId_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedCounterpartyName_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedCounterpartyId_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedGarageNumber_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_RelatedGarageId_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_audit_events_SearchText_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_app_users_DisplayName_trgm";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_app_users_NormalizedEmail_trgm";""");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "audit_events");
        }
    }
}
