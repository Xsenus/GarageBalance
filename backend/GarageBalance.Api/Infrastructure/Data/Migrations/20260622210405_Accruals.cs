using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Accruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncomeTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsCanceled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accruals_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accruals_income_types_IncomeTypeId",
                        column: x => x.IncomeTypeId,
                        principalTable: "income_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accruals_AccountingMonth",
                table: "accruals",
                column: "AccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId",
                table: "accruals",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_AccountingMonth_Source",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "AccountingMonth", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accruals_IncomeTypeId",
                table: "accruals",
                column: "IncomeTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accruals");
        }
    }
}
