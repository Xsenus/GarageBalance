using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGaragePeopleCountHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "garage_people_count_periods",
                columns: table => new
                {
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeopleCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garage_people_count_periods", x => new { x.GarageId, x.EffectiveFrom });
                    table.CheckConstraint("CK_garage_people_count_periods_PeopleCount", "\"PeopleCount\" >= 0 AND \"PeopleCount\" <= 1000");
                    table.ForeignKey(
                        name: "FK_garage_people_count_periods_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO garage_people_count_periods ("GarageId", "EffectiveFrom", "PeopleCount")
                SELECT "Id", DATE '0001-01-01', "PeopleCount" FROM garages;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "garage_people_count_periods");
        }
    }
}
