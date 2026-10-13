using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageAccountingStartMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AccountingStartMonth",
                table: "garages",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_garages_AccountingStartMonth",
                table: "garages",
                sql: "\"AccountingStartMonth\" IS NULL OR (EXTRACT(DAY FROM \"AccountingStartMonth\") = 1 AND \"AccountingStartMonth\" >= DATE '1900-01-01' AND \"AccountingStartMonth\" <= DATE '9999-12-01')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_garages_AccountingStartMonth",
                table: "garages");

            migrationBuilder.DropColumn(
                name: "AccountingStartMonth",
                table: "garages");
        }
    }
}
