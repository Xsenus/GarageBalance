using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierAccruals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_accruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsCanceled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_accruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_accruals_expense_types_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "expense_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_accruals_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_AccountingMonth",
                table: "supplier_accruals",
                column: "AccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_ExpenseTypeId",
                table: "supplier_accruals",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_SupplierId",
                table: "supplier_accruals",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_SupplierId_ExpenseTypeId_AccountingMonth_~",
                table: "supplier_accruals",
                columns: new[] { "SupplierId", "ExpenseTypeId", "AccountingMonth", "Source", "DocumentNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_accruals");
        }
    }
}
