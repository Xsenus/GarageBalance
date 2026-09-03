using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveStaffSalaryHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "staff_employment_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_employment_periods", x => x.Id);
                    table.CheckConstraint("CK_staff_employment_periods_Dates", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                    table.ForeignKey(
                        name: "FK_staff_employment_periods_staff_members_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "staff_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_salary_rate_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff_salary_rate_periods", x => x.Id);
                    table.CheckConstraint("CK_staff_salary_rate_periods_Rate", "\"Rate\" >= 0");
                    table.ForeignKey(
                        name: "FK_staff_salary_rate_periods_staff_members_StaffMemberId",
                        column: x => x.StaffMemberId,
                        principalTable: "staff_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO staff_salary_rate_periods ("Id", "StaffMemberId", "EffectiveFrom", "Rate", "CreatedAtUtc")
                SELECT gen_random_uuid(), member."Id",
                       date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'Asia/Novosibirsk')::date,
                       member."Rate", member."CreatedAtUtc"
                FROM staff_members member;

                INSERT INTO staff_employment_periods ("Id", "StaffMemberId", "EffectiveFrom", "EffectiveTo", "CreatedAtUtc")
                SELECT gen_random_uuid(), member."Id",
                       date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'Asia/Novosibirsk')::date,
                       CASE WHEN member."IsArchived"
                            THEN GREATEST(
                                date_trunc('month', member."CreatedAtUtc" AT TIME ZONE 'Asia/Novosibirsk')::date,
                                date_trunc('month', member."UpdatedAtUtc" AT TIME ZONE 'Asia/Novosibirsk')::date)
                            ELSE NULL
                       END,
                       member."CreatedAtUtc"
                FROM staff_members member;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_staff_employment_periods_StaffMemberId",
                table: "staff_employment_periods",
                column: "StaffMemberId",
                unique: true,
                filter: "\"EffectiveTo\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_staff_employment_periods_StaffMemberId_EffectiveFrom",
                table: "staff_employment_periods",
                columns: new[] { "StaffMemberId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_staff_salary_rate_periods_StaffMemberId_EffectiveFrom",
                table: "staff_salary_rate_periods",
                columns: new[] { "StaffMemberId", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_employment_periods");

            migrationBuilder.DropTable(
                name: "staff_salary_rate_periods");
        }
    }
}
