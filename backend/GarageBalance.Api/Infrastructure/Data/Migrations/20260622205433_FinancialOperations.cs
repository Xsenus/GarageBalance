using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinancialOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AccountingMonth = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    GarageId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncomeTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpenseTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsCanceled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_financial_operations_expense_types_ExpenseTypeId",
                        column: x => x.ExpenseTypeId,
                        principalTable: "expense_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_operations_garages_GarageId",
                        column: x => x.GarageId,
                        principalTable: "garages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_operations_income_types_IncomeTypeId",
                        column: x => x.IncomeTypeId,
                        principalTable: "income_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_financial_operations_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_AccountingMonth",
                table: "financial_operations",
                column: "AccountingMonth");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_ExpenseTypeId",
                table: "financial_operations",
                column: "ExpenseTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_GarageId",
                table: "financial_operations",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_IncomeTypeId",
                table: "financial_operations",
                column: "IncomeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationDate",
                table: "financial_operations",
                column: "OperationDate");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationKind",
                table: "financial_operations",
                column: "OperationKind");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_DocumentNu~",
                table: "financial_operations",
                columns: new[] { "OperationKind", "OperationDate", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_SupplierId",
                table: "financial_operations",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_operations");
        }
    }
}
