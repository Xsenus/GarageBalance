using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFinanceAndFundQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_supplier_accruals_AccountingMonth_SupplierId_Id",
                table: "supplier_accruals",
                columns: new[] { "AccountingMonth", "SupplierId", "Id" },
                descending: new[] { true, false, false },
                filter: "\"IsCanceled\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_fund_operations_FundId_CreatedAtUtc_Id",
                table: "fund_operations",
                columns: new[] { "FundId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~",
                table: "financial_operations",
                columns: new[] { "GarageId", "IncomeTypeId", "OperationDate", "CreatedAtUtc" },
                filter: "\"IsCanceled\" = false AND \"OperationKind\" = 'income'");

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_Id",
                table: "financial_operations",
                columns: new[] { "OperationKind", "OperationDate", "Id" },
                descending: new[] { false, true, false },
                filter: "\"IsCanceled\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_AccountingMonth_GarageId_Id",
                table: "accruals",
                columns: new[] { "AccountingMonth", "GarageId", "Id" },
                descending: new[] { true, false, false },
                filter: "\"IsCanceled\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc",
                table: "accruals",
                columns: new[] { "GarageId", "IncomeTypeId", "DueDate", "CreatedAtUtc" },
                filter: "\"IsCanceled\" = false AND \"DueDateNeedsReview\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_accrual_payment_allocations_AccrualId_FinancialOperationId",
                table: "accrual_payment_allocations",
                columns: new[] { "AccrualId", "FinancialOperationId" },
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_supplier_accruals_AccountingMonth_SupplierId_Id",
                table: "supplier_accruals");

            migrationBuilder.DropIndex(
                name: "IX_fund_operations_FundId_CreatedAtUtc_Id",
                table: "fund_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_financial_operations_OperationKind_OperationDate_Id",
                table: "financial_operations");

            migrationBuilder.DropIndex(
                name: "IX_accruals_AccountingMonth_GarageId_Id",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accrual_payment_allocations_AccrualId_FinancialOperationId",
                table: "accrual_payment_allocations");
        }
    }
}
