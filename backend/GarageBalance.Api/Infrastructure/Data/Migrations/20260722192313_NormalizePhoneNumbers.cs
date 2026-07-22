using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizePhoneNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            NormalizePhones(migrationBuilder, "owners");
            NormalizePhones(migrationBuilder, "suppliers");
            NormalizePhones(migrationBuilder, "supplier_contacts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Formatting is intentionally irreversible: all recognized values keep the same digits.
        }

        private static void NormalizePhones(MigrationBuilder migrationBuilder, string tableName)
        {
            migrationBuilder.Sql($$"""
                WITH phone_digits AS (
                    SELECT "Id", regexp_replace("Phone", '[^0-9]', '', 'g') AS digits
                    FROM "{{tableName}}"
                    WHERE "Phone" IS NOT NULL
                ),
                normalized_phones AS (
                    SELECT
                        "Id",
                        CASE
                            WHEN length(digits) = 10 THEN digits
                            WHEN length(digits) = 11 AND left(digits, 1) IN ('7', '8') THEN substring(digits FROM 2)
                            ELSE NULL
                        END AS national_number
                    FROM phone_digits
                )
                UPDATE "{{tableName}}" AS target
                SET "Phone" = '+7 (' || substring(source.national_number FROM 1 FOR 3) || ') '
                    || substring(source.national_number FROM 4 FOR 3) || '-'
                    || substring(source.national_number FROM 7 FOR 2) || '-'
                    || substring(source.national_number FROM 9 FOR 2)
                FROM normalized_phones AS source
                WHERE target."Id" = source."Id"
                  AND source.national_number IS NOT NULL
                  AND left(source.national_number, 1) IN ('3', '4', '8', '9');
                """);
        }
    }
}
