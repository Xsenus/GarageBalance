using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAccountingTypeCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_income_types_Code",
                table: "income_types");

            migrationBuilder.DropIndex(
                name: "IX_expense_types_Code",
                table: "expense_types");

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    repaired_count integer := 0;
                    affected_count integer := 0;
                BEGIN
                    UPDATE income_types
                    SET
                        "Code" = CASE
                            WHEN NULLIF(LOWER(BTRIM("Code")), '') IS NULL THEN NULL
                            WHEN NULLIF(LOWER(BTRIM("Code")), '') !~ '^[a-z][a-z0-9_]*$' THEN NULL
                            WHEN NOT "IsSystem" AND NULLIF(LOWER(BTRIM("Code")), '') = ANY (ARRAY['water','trash','electricity','membership','target','entry','connection','outdoor_lighting','penalty','notice','fee_campaign','other_payments','other_income','debt_transfer']) THEN NULL
                            ELSE NULLIF(LOWER(BTRIM("Code")), '')
                        END,
                        "UpdatedAtUtc" = NOW()
                    WHERE "Code" IS DISTINCT FROM CASE
                        WHEN NULLIF(LOWER(BTRIM("Code")), '') IS NULL THEN NULL
                        WHEN NULLIF(LOWER(BTRIM("Code")), '') !~ '^[a-z][a-z0-9_]*$' THEN NULL
                        WHEN NOT "IsSystem" AND NULLIF(LOWER(BTRIM("Code")), '') = ANY (ARRAY['water','trash','electricity','membership','target','entry','connection','outdoor_lighting','penalty','notice','fee_campaign','other_payments','other_income','debt_transfer']) THEN NULL
                        ELSE NULLIF(LOWER(BTRIM("Code")), '')
                    END;
                    GET DIAGNOSTICS affected_count = ROW_COUNT;
                    repaired_count := repaired_count + affected_count;

                    WITH ranked AS (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "Code"
                            ORDER BY "IsSystem" DESC, "CreatedAtUtc", "Id") AS row_number
                        FROM income_types
                        WHERE NOT "IsArchived" AND "Code" IS NOT NULL
                    )
                    UPDATE income_types item
                    SET "Code" = NULL, "UpdatedAtUtc" = NOW()
                    FROM ranked
                    WHERE item."Id" = ranked."Id" AND ranked.row_number > 1;
                    GET DIAGNOSTICS affected_count = ROW_COUNT;
                    repaired_count := repaired_count + affected_count;

                    UPDATE expense_types
                    SET
                        "Code" = CASE
                            WHEN NULLIF(LOWER(BTRIM("Code")), '') IS NULL THEN NULL
                            WHEN NULLIF(LOWER(BTRIM("Code")), '') !~ '^[a-z][a-z0-9_]*$' THEN NULL
                            WHEN NOT "IsSystem" AND NULLIF(LOWER(BTRIM("Code")), '') = ANY (ARRAY['electricity','trash_removal','water_supply','bank','legal','salary','other','penalty']) THEN NULL
                            ELSE NULLIF(LOWER(BTRIM("Code")), '')
                        END,
                        "UpdatedAtUtc" = NOW()
                    WHERE "Code" IS DISTINCT FROM CASE
                        WHEN NULLIF(LOWER(BTRIM("Code")), '') IS NULL THEN NULL
                        WHEN NULLIF(LOWER(BTRIM("Code")), '') !~ '^[a-z][a-z0-9_]*$' THEN NULL
                        WHEN NOT "IsSystem" AND NULLIF(LOWER(BTRIM("Code")), '') = ANY (ARRAY['electricity','trash_removal','water_supply','bank','legal','salary','other','penalty']) THEN NULL
                        ELSE NULLIF(LOWER(BTRIM("Code")), '')
                    END;
                    GET DIAGNOSTICS affected_count = ROW_COUNT;
                    repaired_count := repaired_count + affected_count;

                    WITH ranked AS (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "Code"
                            ORDER BY "IsSystem" DESC, "CreatedAtUtc", "Id") AS row_number
                        FROM expense_types
                        WHERE NOT "IsArchived" AND "Code" IS NOT NULL
                    )
                    UPDATE expense_types item
                    SET "Code" = NULL, "UpdatedAtUtc" = NOW()
                    FROM ranked
                    WHERE item."Id" = ranked."Id" AND ranked.row_number > 1;
                    GET DIAGNOSTICS affected_count = ROW_COUNT;
                    repaired_count := repaired_count + affected_count;

                    IF repaired_count > 0 THEN
                        INSERT INTO audit_events (
                            "Id", "CreatedAtUtc", "Action", "Section", "ActionKind", "EntityType", "Summary", "MetadataJson")
                        VALUES (
                            'f6dab7a9-f01f-49ef-a57c-7ad82862c9aa', NOW(),
                            'dictionary.accounting_type_codes_repaired', 'dictionary', 'update', 'accounting_type',
                            'Нормализованы конфликтующие или некорректные коды видов поступлений и расходов.',
                            json_build_object('repairedCount', repaired_count)::text)
                        ON CONFLICT ("Id") DO NOTHING;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Code",
                table: "income_types",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsArchived\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_income_types_Code_Format",
                table: "income_types",
                sql: "\"Code\" IS NULL OR \"Code\" ~ '^[a-z][a-z0-9_]*$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_income_types_Reserved_Code_System_Only",
                table: "income_types",
                sql: "\"IsSystem\" OR \"Code\" IS NULL OR \"Code\" <> ALL (ARRAY['water','trash','electricity','membership','target','entry','connection','outdoor_lighting','penalty','notice','fee_campaign','other_payments','other_income','debt_transfer'])");

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Code",
                table: "expense_types",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsArchived\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_expense_types_Code_Format",
                table: "expense_types",
                sql: "\"Code\" IS NULL OR \"Code\" ~ '^[a-z][a-z0-9_]*$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_expense_types_Reserved_Code_System_Only",
                table: "expense_types",
                sql: "\"IsSystem\" OR \"Code\" IS NULL OR \"Code\" <> ALL (ARRAY['electricity','trash_removal','water_supply','bank','legal','salary','other','penalty'])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_income_types_Code",
                table: "income_types");

            migrationBuilder.DropCheckConstraint(
                name: "CK_income_types_Code_Format",
                table: "income_types");

            migrationBuilder.DropCheckConstraint(
                name: "CK_income_types_Reserved_Code_System_Only",
                table: "income_types");

            migrationBuilder.DropIndex(
                name: "IX_expense_types_Code",
                table: "expense_types");

            migrationBuilder.DropCheckConstraint(
                name: "CK_expense_types_Code_Format",
                table: "expense_types");

            migrationBuilder.DropCheckConstraint(
                name: "CK_expense_types_Reserved_Code_System_Only",
                table: "expense_types");

            migrationBuilder.CreateIndex(
                name: "IX_income_types_Code",
                table: "income_types",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_expense_types_Code",
                table: "expense_types",
                column: "Code");
        }
    }
}
