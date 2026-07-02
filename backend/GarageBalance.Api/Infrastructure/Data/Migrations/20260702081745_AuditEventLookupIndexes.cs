using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorUserId",
                table: "audit_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedCounterpartyName",
                table: "audit_events",
                column: "RelatedCounterpartyName");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedDocumentNumber",
                table: "audit_events",
                column: "RelatedDocumentNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_ActorUserId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedCounterpartyName",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedDocumentNumber",
                table: "audit_events");
        }
    }
}
