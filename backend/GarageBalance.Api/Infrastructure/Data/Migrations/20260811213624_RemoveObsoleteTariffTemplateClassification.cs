using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveObsoleteTariffTemplateClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tariffs_IsTemplate_IsArchived_EffectiveFrom",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "IsTemplate",
                table: "tariffs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemplate",
                table: "tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_IsTemplate_IsArchived_EffectiveFrom",
                table: "tariffs",
                columns: new[] { "IsTemplate", "IsArchived", "EffectiveFrom" });
        }
    }
}
