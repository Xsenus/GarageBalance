using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventRelatedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EntityDisplayName",
                table: "audit_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedAccountingMonth",
                table: "audit_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedCounterpartyId",
                table: "audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedCounterpartyName",
                table: "audit_events",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedDocumentId",
                table: "audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedDocumentNumber",
                table: "audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedGarageId",
                table: "audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedGarageNumber",
                table: "audit_events",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedAccountingMonth",
                table: "audit_events",
                column: "RelatedAccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedCounterpartyId",
                table: "audit_events",
                column: "RelatedCounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedDocumentId",
                table: "audit_events",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedGarageId",
                table: "audit_events",
                column: "RelatedGarageId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_RelatedGarageNumber",
                table: "audit_events",
                column: "RelatedGarageNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedAccountingMonth",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedCounterpartyId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedDocumentId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedGarageId",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_RelatedGarageNumber",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "EntityDisplayName",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedAccountingMonth",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedCounterpartyId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedCounterpartyName",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedDocumentId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedDocumentNumber",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedGarageId",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "RelatedGarageNumber",
                table: "audit_events");
        }
    }
}
