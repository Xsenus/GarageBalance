using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountingTypesAndTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expense_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "income_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_income_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CalculationBase = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tariffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Code",
                table: "expense_types",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Name",
                table: "expense_types",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Code",
                table: "income_types",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Name",
                table: "income_types",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_CalculationBase",
                table: "tariffs",
                column: "CalculationBase");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_EffectiveFrom",
                table: "tariffs",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_Name_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "Name", "EffectiveFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expense_types");

            migrationBuilder.DropTable(
                name: "income_types");

            migrationBuilder.DropTable(
                name: "tariffs");
        }
    }
}
