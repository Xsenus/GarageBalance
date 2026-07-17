using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccrualPaymentAllocationAmountConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_accrual_payment_allocations_Amount_Positive",
                table: "accrual_payment_allocations",
                sql: "\"Amount\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_accrual_payment_allocations_Amount_Positive",
                table: "accrual_payment_allocations");
        }
    }
}
