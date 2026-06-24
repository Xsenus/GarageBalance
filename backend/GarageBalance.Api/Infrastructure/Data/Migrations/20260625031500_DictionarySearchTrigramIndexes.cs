using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GarageBalanceDbContext))]
    [Migration("20260625031500_DictionarySearchTrigramIndexes")]
    public partial class DictionarySearchTrigramIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

            CreateTrigramIndex(migrationBuilder, "IX_owners_LastName_trgm", "owners", """lower("LastName")""");
            CreateTrigramIndex(migrationBuilder, "IX_owners_FirstName_trgm", "owners", """lower("FirstName")""");
            CreateTrigramIndex(migrationBuilder, "IX_owners_MiddleName_trgm", "owners", """lower("MiddleName")""");
            CreateTrigramIndex(migrationBuilder, "IX_owners_Phone_trgm", "owners", """lower("Phone")""");
            CreateTrigramIndex(migrationBuilder, "IX_owners_FullName_trgm", "owners", """lower("LastName" || ' ' || "FirstName" || ' ' || COALESCE("MiddleName", ''))""");
            CreateTrigramIndex(migrationBuilder, "IX_garages_Number_trgm", "garages", """lower("Number")""");
            CreateTrigramIndex(migrationBuilder, "IX_suppliers_Name_trgm", "suppliers", """lower("Name")""");
            CreateTrigramIndex(migrationBuilder, "IX_suppliers_Inn_trgm", "suppliers", """lower("Inn")""");
            CreateTrigramIndex(migrationBuilder, "IX_suppliers_ContactPerson_trgm", "suppliers", """lower("ContactPerson")""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropIndex(migrationBuilder, "IX_suppliers_ContactPerson_trgm");
            DropIndex(migrationBuilder, "IX_suppliers_Inn_trgm");
            DropIndex(migrationBuilder, "IX_suppliers_Name_trgm");
            DropIndex(migrationBuilder, "IX_garages_Number_trgm");
            DropIndex(migrationBuilder, "IX_owners_FullName_trgm");
            DropIndex(migrationBuilder, "IX_owners_Phone_trgm");
            DropIndex(migrationBuilder, "IX_owners_MiddleName_trgm");
            DropIndex(migrationBuilder, "IX_owners_FirstName_trgm");
            DropIndex(migrationBuilder, "IX_owners_LastName_trgm");
        }

        private static void CreateTrigramIndex(MigrationBuilder migrationBuilder, string name, string table, string expression)
        {
            migrationBuilder.Sql($$"""
                CREATE INDEX IF NOT EXISTS "{{name}}"
                ON {{table}} USING gin (({{expression}}) gin_trgm_ops)
                WHERE "IsArchived" = FALSE;
                """);
        }

        private static void DropIndex(MigrationBuilder migrationBuilder, string name)
        {
            migrationBuilder.Sql($$"""DROP INDEX IF EXISTS "{{name}}";""");
        }
    }
}
