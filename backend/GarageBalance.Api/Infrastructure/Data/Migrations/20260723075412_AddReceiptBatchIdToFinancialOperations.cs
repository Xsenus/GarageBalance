using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptBatchIdToFinancialOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptBatchId",
                table: "financial_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_ReceiptBatchId",
                table: "financial_operations",
                column: "ReceiptBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_operations_ReceiptBatchId",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "ReceiptBatchId",
                table: "financial_operations");
        }
    }
}
