using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

/// <summary>
/// Expand phase for rollback compatibility. The runtime no longer uses this table,
/// but the previous release still expects it. Removal belongs to a later contract release.
/// </summary>
[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260804113000_RestoreLegacyFormStatesCompatibility")]
public sealed class RestoreLegacyFormStatesCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS form_states (
                "Id" uuid NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "PayloadJson" text NOT NULL,
                "Scope" character varying(120) NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedByUserId" uuid NULL,
                CONSTRAINT "PK_form_states" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_form_states_Scope"
                ON form_states ("Scope");
            CREATE INDEX IF NOT EXISTS "IX_form_states_UpdatedAtUtc"
                ON form_states ("UpdatedAtUtc");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // EF then executes RemoveLegacyFormStates.Down and recreates the previous schema.
        // Production binary rollback restores the pre-release dump instead of running Down.
        migrationBuilder.DropTable(name: "form_states");
    }
}
