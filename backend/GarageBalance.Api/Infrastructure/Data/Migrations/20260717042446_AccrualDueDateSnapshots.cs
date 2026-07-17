using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccrualDueDateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "accruals",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "OverdueFromDate",
                table: "accruals",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            if (ActiveProvider.Contains("Npgsql", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(
                    """
                    UPDATE accruals
                    SET "DueDate" = (date_trunc('month', "AccountingMonth") + interval '2 months - 1 day')::date,
                        "OverdueFromDate" = (date_trunc('month', "AccountingMonth") + interval '3 months')::date;
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE accruals
                    SET "DueDate" = date("AccountingMonth", 'start of month', '+2 months', '-1 day'),
                        "OverdueFromDate" = date("AccountingMonth", 'start of month', '+3 months');
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_accruals_DueDate",
                table: "accruals",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_accruals_GarageId_OverdueFromDate_IsCanceled",
                table: "accruals",
                columns: new[] { "GarageId", "OverdueFromDate", "IsCanceled" });

            migrationBuilder.CreateIndex(
                name: "IX_accruals_OverdueFromDate",
                table: "accruals",
                column: "OverdueFromDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accruals_DueDate",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_GarageId_OverdueFromDate_IsCanceled",
                table: "accruals");

            migrationBuilder.DropIndex(
                name: "IX_accruals_OverdueFromDate",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "accruals");

            migrationBuilder.DropColumn(
                name: "OverdueFromDate",
                table: "accruals");
        }
    }
}
