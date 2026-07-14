using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFundDashboardQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_financial_operations_IsCanceled_OperationKind",
                table: "financial_operations",
                columns: new[] { "IsCanceled", "OperationKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_operations_IsCanceled_OperationKind",
                table: "financial_operations");
        }
    }
}
