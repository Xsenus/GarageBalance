using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageDiscountAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "FinancialOperationId",
                table: "accrual_payment_allocations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CreditAccrualId",
                table: "accrual_payment_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accrual_payment_allocations_CreditAccrualId_AccrualId",
                table: "accrual_payment_allocations",
                columns: new[] { "CreditAccrualId", "AccrualId" },
                unique: true,
                filter: "\"IsActive\" = true AND \"CreditAccrualId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_accrual_payment_allocations_Source",
                table: "accrual_payment_allocations",
                sql: "(\"FinancialOperationId\" IS NULL) <> (\"CreditAccrualId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_accrual_payment_allocations_accruals_CreditAccrualId",
                table: "accrual_payment_allocations",
                column: "CreditAccrualId",
                principalTable: "accruals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM accruals WHERE "Amount" < 0)
                    THEN RAISE EXCEPTION 'Discount history exists. Restore the pre-release backup to roll back safely.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_accrual_payment_allocations_accruals_CreditAccrualId",
                table: "accrual_payment_allocations");

            migrationBuilder.DropIndex(
                name: "IX_accrual_payment_allocations_CreditAccrualId_AccrualId",
                table: "accrual_payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_accrual_payment_allocations_Source",
                table: "accrual_payment_allocations");

            migrationBuilder.DropColumn(
                name: "CreditAccrualId",
                table: "accrual_payment_allocations");

            migrationBuilder.AlterColumn<Guid>(
                name: "FinancialOperationId",
                table: "accrual_payment_allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
