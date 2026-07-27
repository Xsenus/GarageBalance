using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBankBalanceOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_bank_balance_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OperationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_bank_balance_operations", x => x.Id);
                    table.CheckConstraint("CK_cash_bank_balance_operations_Account", "\"Account\" IN ('cash', 'bank')");
                    table.CheckConstraint("CK_cash_bank_balance_operations_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_cash_bank_balance_operations_Direction", "\"Direction\" IN ('increase', 'decrease')");
                    table.CheckConstraint("CK_cash_bank_balance_operations_OperationKind", "\"OperationKind\" IN ('opening_balance', 'adjustment')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_balance_operations_Account_OperationKind",
                table: "cash_bank_balance_operations",
                columns: new[] { "Account", "OperationKind" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_balance_operations_ActorUserId",
                table: "cash_bank_balance_operations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_balance_operations_CreatedAtUtc",
                table: "cash_bank_balance_operations",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_cash_bank_balance_operations_OperationDate",
                table: "cash_bank_balance_operations",
                column: "OperationDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_bank_balance_operations");
        }
    }
}
