using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FeeCampaignIncomeType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IncomeTypeId",
                table: "fee_campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    fallback_income_type_id uuid;
                BEGIN
                    SELECT "Id"
                    INTO fallback_income_type_id
                    FROM income_types
                    WHERE "IsArchived" = FALSE AND "Code" IN ('target', 'fee_campaign')
                    ORDER BY CASE WHEN "Code" = 'target' THEN 0 ELSE 1 END
                    LIMIT 1;

                    IF fallback_income_type_id IS NULL THEN
                        fallback_income_type_id := 'd420be1c-97bd-42e4-8d77-6476beee40c2';

                        INSERT INTO income_types ("Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                        SELECT fallback_income_type_id, 'Сборы', 'fee_campaign', TRUE, FALSE, NOW(), NOW()
                        WHERE NOT EXISTS (
                            SELECT 1
                            FROM income_types
                            WHERE "IsArchived" = FALSE AND ("Name" = 'Сборы' OR "Code" = 'fee_campaign')
                        );

                        SELECT "Id"
                        INTO fallback_income_type_id
                        FROM income_types
                        WHERE "IsArchived" = FALSE AND ("Code" = 'fee_campaign' OR "Name" = 'Сборы')
                        LIMIT 1;
                    END IF;

                    UPDATE fee_campaigns
                    SET "IncomeTypeId" = fallback_income_type_id
                    WHERE "IncomeTypeId" IS NULL;
                END $$;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "IncomeTypeId",
                table: "fee_campaigns",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_campaigns_IncomeTypeId",
                table: "fee_campaigns",
                column: "IncomeTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_fee_campaigns_income_types_IncomeTypeId",
                table: "fee_campaigns",
                column: "IncomeTypeId",
                principalTable: "income_types",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fee_campaigns_income_types_IncomeTypeId",
                table: "fee_campaigns");

            migrationBuilder.DropIndex(
                name: "IX_fee_campaigns_IncomeTypeId",
                table: "fee_campaigns");

            migrationBuilder.DropColumn(
                name: "IncomeTypeId",
                table: "fee_campaigns");

            migrationBuilder.Sql("""
                DELETE FROM income_types
                WHERE "IsSystem" = TRUE AND "Code" = 'fee_campaign'
                AND NOT EXISTS (
                    SELECT 1
                    FROM financial_operations
                    WHERE "IncomeTypeId" = income_types."Id"
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM accruals
                    WHERE "IncomeTypeId" = income_types."Id"
                );
                """);
        }
    }
}
