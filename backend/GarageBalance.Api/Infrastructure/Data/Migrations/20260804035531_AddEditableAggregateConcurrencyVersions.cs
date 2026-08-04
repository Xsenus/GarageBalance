using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEditableAggregateConcurrencyVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "tariffs",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "suppliers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "garages",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "funds",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "charge_service_settings",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "application_settings",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "app_users",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "garages");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "charge_service_settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "application_settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "app_users");
        }
    }
}
