using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveDeletedFunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_funds_NormalizedName",
                table: "funds");

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "funds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_funds_NormalizedName",
                table: "funds",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsArchived\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_funds_NormalizedName",
                table: "funds");

            migrationBuilder.Sql(
                """
                UPDATE funds AS archived
                SET
                    "Name" = LEFT(archived."Name", 150) || ' (архив ' || archived."Id"::text || ')',
                    "NormalizedName" = LEFT(archived."NormalizedName", 150) || ' [АРХИВ ' || archived."Id"::text || ']'
                WHERE archived."IsArchived"
                  AND EXISTS (
                      SELECT 1
                      FROM funds AS active
                      WHERE NOT active."IsArchived"
                        AND active."NormalizedName" = archived."NormalizedName"
                  );
                """);

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "funds");

            migrationBuilder.CreateIndex(
                name: "IX_funds_NormalizedName",
                table: "funds",
                column: "NormalizedName",
                unique: true);
        }
    }
}
