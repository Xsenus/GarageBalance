using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// Adds optional episodic recipients and an auditable negative-fund confirmation.
namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEpisodicExpenseRecipientAndFundConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterpartyName",
                table: "financial_operations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NegativeFundBalanceConfirmed",
                table: "financial_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterpartyName",
                table: "financial_operations");

            migrationBuilder.DropColumn(
                name: "NegativeFundBalanceConfirmed",
                table: "financial_operations");
        }
    }
}
