using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations;

[DbContext(typeof(GarageBalanceDbContext))]
[Migration("20260730013000_ExpandDictionarySearchIndexes")]
public sealed class ExpandDictionarySearchIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

        string[] replacedIndexes =
        [
            "IX_owners_LastName_trgm",
            "IX_owners_FirstName_trgm",
            "IX_owners_MiddleName_trgm",
            "IX_owners_Phone_trgm",
            "IX_owners_FullName_trgm",
            "IX_garages_Number_trgm",
            "IX_suppliers_Name_trgm",
            "IX_suppliers_Inn_trgm",
            "IX_suppliers_ContactPerson_trgm"
        ];
        foreach (var indexName in replacedIndexes)
        {
            DropIndex(migrationBuilder, indexName);
        }

        CreateTrigramIndex(migrationBuilder, "IX_owners_LastName_trgm", "owners", "\"LastName\"");
        CreateTrigramIndex(migrationBuilder, "IX_owners_FirstName_trgm", "owners", "\"FirstName\"");
        CreateTrigramIndex(migrationBuilder, "IX_owners_MiddleName_trgm", "owners", "\"MiddleName\"");
        CreateTrigramIndex(migrationBuilder, "IX_owners_Phone_trgm", "owners", "\"Phone\"");
        CreateTrigramIndex(
            migrationBuilder,
            "IX_owners_FullName_trgm",
            "owners",
            """("LastName" || ' ' || "FirstName" || ' ' || COALESCE("MiddleName", ''))""");
        CreateTrigramIndex(migrationBuilder, "IX_garages_Number_trgm", "garages", "\"Number\"");
        CreateTrigramIndex(migrationBuilder, "IX_suppliers_Name_trgm", "suppliers", "\"Name\"");
        CreateTrigramIndex(migrationBuilder, "IX_suppliers_Inn_trgm", "suppliers", "\"Inn\"");
        CreateTrigramIndex(migrationBuilder, "IX_suppliers_ContactPerson_trgm", "suppliers", "\"ContactPerson\"");
        CreateTrigramIndex(migrationBuilder, "IX_supplier_groups_Name_trgm", "supplier_groups", "\"Name\"");
        CreateTrigramIndex(
            migrationBuilder,
            "IX_charge_service_settings_Name_trgm",
            "charge_service_settings",
            "\"Name\"");
        CreateTrigramIndex(migrationBuilder, "IX_supplier_contacts_FullName_trgm", "supplier_contacts", "\"FullName\"");
        CreateTrigramIndex(migrationBuilder, "IX_supplier_contacts_Position_trgm", "supplier_contacts", "\"Position\"");
        CreateTrigramIndex(migrationBuilder, "IX_supplier_contacts_Phone_trgm", "supplier_contacts", "\"Phone\"");
        CreateTrigramIndex(migrationBuilder, "IX_supplier_contacts_Email_trgm", "supplier_contacts", "\"Email\"");
        CreateTrigramIndex(migrationBuilder, "IX_staff_departments_Name_trgm", "staff_departments", "\"Name\"");
        CreateTrigramIndex(migrationBuilder, "IX_staff_members_FullName_trgm", "staff_members", "\"FullName\"");

        migrationBuilder.Sql(
            """
            CREATE INDEX "IX_supplier_contacts_PrimaryContact"
            ON supplier_contacts (
                "SupplierId",
                (("Status" = 'Работает')) DESC,
                "FullName",
                "Id")
            WHERE "IsArchived" = FALSE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        DropIndex(migrationBuilder, "IX_supplier_contacts_PrimaryContact");
        DropIndex(migrationBuilder, "IX_staff_members_FullName_trgm");
        DropIndex(migrationBuilder, "IX_staff_departments_Name_trgm");
        DropIndex(migrationBuilder, "IX_supplier_contacts_Email_trgm");
        DropIndex(migrationBuilder, "IX_supplier_contacts_Phone_trgm");
        DropIndex(migrationBuilder, "IX_supplier_contacts_Position_trgm");
        DropIndex(migrationBuilder, "IX_supplier_contacts_FullName_trgm");
        DropIndex(migrationBuilder, "IX_charge_service_settings_Name_trgm");
        DropIndex(migrationBuilder, "IX_supplier_groups_Name_trgm");
        DropIndex(migrationBuilder, "IX_suppliers_ContactPerson_trgm");
        DropIndex(migrationBuilder, "IX_suppliers_Inn_trgm");
        DropIndex(migrationBuilder, "IX_suppliers_Name_trgm");
        DropIndex(migrationBuilder, "IX_garages_Number_trgm");
        DropIndex(migrationBuilder, "IX_owners_FullName_trgm");
        DropIndex(migrationBuilder, "IX_owners_Phone_trgm");
        DropIndex(migrationBuilder, "IX_owners_MiddleName_trgm");
        DropIndex(migrationBuilder, "IX_owners_FirstName_trgm");
        DropIndex(migrationBuilder, "IX_owners_LastName_trgm");

        CreateLegacyTrigramIndex(migrationBuilder, "IX_owners_LastName_trgm", "owners", """lower("LastName")""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_owners_FirstName_trgm", "owners", """lower("FirstName")""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_owners_MiddleName_trgm", "owners", """lower("MiddleName")""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_owners_Phone_trgm", "owners", """lower("Phone")""");
        CreateLegacyTrigramIndex(
            migrationBuilder,
            "IX_owners_FullName_trgm",
            "owners",
            """lower("LastName" || ' ' || "FirstName" || ' ' || COALESCE("MiddleName", ''))""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_garages_Number_trgm", "garages", """lower("Number")""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_suppliers_Name_trgm", "suppliers", """lower("Name")""");
        CreateLegacyTrigramIndex(migrationBuilder, "IX_suppliers_Inn_trgm", "suppliers", """lower("Inn")""");
        CreateLegacyTrigramIndex(
            migrationBuilder,
            "IX_suppliers_ContactPerson_trgm",
            "suppliers",
            """lower("ContactPerson")""");
    }

    private static void CreateTrigramIndex(
        MigrationBuilder migrationBuilder,
        string name,
        string table,
        string expression)
    {
        migrationBuilder.Sql(
            $$"""
            CREATE INDEX "{{name}}"
            ON {{table}} USING gin (({{expression}}) gin_trgm_ops);
            """);
    }

    private static void CreateLegacyTrigramIndex(
        MigrationBuilder migrationBuilder,
        string name,
        string table,
        string expression)
    {
        migrationBuilder.Sql(
            $$"""
            CREATE INDEX "{{name}}"
            ON {{table}} USING gin (({{expression}}) gin_trgm_ops)
            WHERE "IsArchived" = FALSE;
            """);
    }

    private static void DropIndex(MigrationBuilder migrationBuilder, string name)
    {
        migrationBuilder.Sql($$"""DROP INDEX IF EXISTS "{{name}}";""");
    }
}
