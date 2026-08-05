using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable


namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupplierExpenseFundOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseFundId",
                table: "suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_ExpenseFundId",
                table: "suppliers",
                column: "ExpenseFundId");

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_funds_ExpenseFundId",
                table: "suppliers",
                column: "ExpenseFundId",
                principalTable: "funds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_funds_ExpenseFundId",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_ExpenseFundId",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "ExpenseFundId",
                table: "suppliers");
        }
    }
}
