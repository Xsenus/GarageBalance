using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreserveMeterAccountingBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAccountingBaseline",
                table: "meter_devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_meter_devices_accounting_baseline",
                table: "meter_devices",
                columns: new[] { "GarageId", "MeterKind" },
                unique: true,
                filter: "\"IsAccountingBaseline\" = true AND \"IsCanceled\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM meter_devices WHERE "IsAccountingBaseline") THEN
                        RAISE EXCEPTION 'Preserved meter baselines exist. Restore the verified pre-cleanup backup for rollback.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_meter_devices_accounting_baseline",
                table: "meter_devices");

            migrationBuilder.DropColumn(
                name: "IsAccountingBaseline",
                table: "meter_devices");
        }
    }
}
