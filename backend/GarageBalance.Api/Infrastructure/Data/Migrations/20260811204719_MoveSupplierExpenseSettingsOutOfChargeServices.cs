using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveSupplierExpenseSettingsOutOfChargeServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseTypeId",
                table: "suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE suppliers AS supplier
                SET "ExpenseTypeId" = service."ExpenseTypeId",
                    "ExpenseFundId" = COALESCE(supplier."ExpenseFundId", service."ExpenseFundId")
                FROM charge_service_settings AS service
                WHERE supplier."ChargeServiceSettingId" = service."Id";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_expense_types_ExpenseTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropForeignKey(
                name: "FK_charge_service_settings_funds_ExpenseFundId",
                table: "charge_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_ExpenseFundId",
                table: "charge_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_charge_service_settings_ExpenseTypeId",
                table: "charge_service_settings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_charge_service_settings_ExpenseFundLink",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "ExpenseFundId",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "ExpenseTypeId",
                table: "charge_service_settings");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_ExpenseTypeId",
                table: "suppliers",
                column: "ExpenseTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_expense_types_ExpenseTypeId",
                table: "suppliers",
                column: "ExpenseTypeId",
                principalTable: "expense_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_expense_types_ExpenseTypeId",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_ExpenseTypeId",
                table: "suppliers");

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseFundId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseTypeId",
                table: "charge_service_settings",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE charge_service_settings AS service
                SET "ExpenseTypeId" = supplier."ExpenseTypeId",
                    "ExpenseFundId" = supplier."ExpenseFundId"
                FROM suppliers AS supplier
                WHERE supplier."ChargeServiceSettingId" = service."Id"
                  AND service."ExpenseTypeId" IS NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ExpenseTypeId",
                table: "suppliers");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_ExpenseFundId",
                table: "charge_service_settings",
                column: "ExpenseFundId");

            migrationBuilder.CreateIndex(
                name: "IX_charge_service_settings_ExpenseTypeId",
                table: "charge_service_settings",
                column: "ExpenseTypeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_charge_service_settings_ExpenseFundLink",
                table: "charge_service_settings",
                sql: "\"ExpenseFundId\" IS NULL OR \"ExpenseTypeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_expense_types_ExpenseTypeId",
                table: "charge_service_settings",
                column: "ExpenseTypeId",
                principalTable: "expense_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_charge_service_settings_funds_ExpenseFundId",
                table: "charge_service_settings",
                column: "ExpenseFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
