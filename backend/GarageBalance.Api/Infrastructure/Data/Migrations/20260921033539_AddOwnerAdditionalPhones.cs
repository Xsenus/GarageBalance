using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerAdditionalPhones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "owner_additional_phones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_owner_additional_phones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_owner_additional_phones_owners_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "owners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_owner_additional_phones_OwnerId_IsArchived_SortOrder",
                table: "owner_additional_phones",
                columns: new[] { "OwnerId", "IsArchived", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_owner_additional_phones_OwnerId_Phone",
                table: "owner_additional_phones",
                columns: new[] { "OwnerId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_owner_additional_phones_Phone",
                table: "owner_additional_phones",
                column: "Phone");

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_owner_additional_phones_Phone_trgm"
                ON owner_additional_phones
                USING gin ("Phone" gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_owner_additional_phones_Phone_trgm\";");

            migrationBuilder.DropTable(
                name: "owner_additional_phones");
        }
    }
}
