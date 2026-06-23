using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageBalance.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GarageBalanceDbContext))]
    [Migration("20260623152000_DefaultAccountingTypes")]
    public partial class DefaultAccountingTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            InsertIncomeType(migrationBuilder, "3e79c525-377d-4168-b8de-f2ff736829df", "Вода", "water");
            InsertIncomeType(migrationBuilder, "a55bf70e-3f02-42f5-9ac3-e4ac7278b1b0", "Мусор", "trash");
            InsertIncomeType(migrationBuilder, "98afc64c-2d57-4276-bc7f-58f0de6ce92e", "Электроэнергия", "electricity");
            InsertIncomeType(migrationBuilder, "f465d904-2b57-4e99-bd88-1bd04b2f6e66", "Членский взнос", "membership");
            InsertIncomeType(migrationBuilder, "cfdbb49e-e6c8-4b91-8a10-f783cd55b4c8", "Целевой взнос", "target");
            InsertIncomeType(migrationBuilder, "19b731c2-43a6-4dcd-9670-ba76a512b091", "Вступительный взнос", "entry");
            InsertIncomeType(migrationBuilder, "95f18e62-7ac8-4aac-b80e-af371a6d9581", "Подключения", "connection");
            InsertIncomeType(migrationBuilder, "9e78f409-6a41-4073-a8af-8d5b4b80d85b", "Штраф", "penalty");
            InsertIncomeType(migrationBuilder, "13965d6a-e589-42af-bf29-f44c704bdd61", "Предписание", "notice");

            InsertExpenseType(migrationBuilder, "ab93f9ed-4f83-40ef-b7b7-4e7ce8aaa000", "Электроэнергия", "electricity");
            InsertExpenseType(migrationBuilder, "18b03fd5-a9da-4ce6-9940-dd9848820d10", "Вывоз мусора", "trash_removal");
            InsertExpenseType(migrationBuilder, "9b2d316c-2503-42ad-acd5-9bf3199c5aae", "Водоснабжение", "water_supply");
            InsertExpenseType(migrationBuilder, "5c8451b8-78fe-412d-8def-123ba8ae47b3", "Банковские расходы", "bank");
            InsertExpenseType(migrationBuilder, "1121b98b-1998-46ad-a4f9-59c9191cd177", "Юридические расходы", "legal");
            InsertExpenseType(migrationBuilder, "5ce3eb74-0605-4ae7-8b52-3da38c693aee", "Зарплата", "salary");
            InsertExpenseType(migrationBuilder, "5ed33ff2-b69f-470c-8378-7fb312af5ed7", "Прочие расходы", "other");
            InsertExpenseType(migrationBuilder, "5322ab4c-24e4-49c0-a24e-32427ac318ca", "Штрафы", "penalty");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM income_types
                WHERE "IsSystem" = TRUE AND "Code" IN ('water', 'trash', 'electricity', 'membership', 'target', 'entry', 'connection', 'penalty', 'notice');

                DELETE FROM expense_types
                WHERE "IsSystem" = TRUE AND "Code" IN ('electricity', 'trash_removal', 'water_supply', 'bank', 'legal', 'salary', 'other', 'penalty');
                """);
        }

        private static void InsertIncomeType(MigrationBuilder migrationBuilder, string id, string name, string code)
        {
            migrationBuilder.Sql($"""
                INSERT INTO income_types ("Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ('{id}', '{name}', '{code}', TRUE, FALSE, TIMESTAMPTZ '2026-06-23T00:00:00Z', TIMESTAMPTZ '2026-06-23T00:00:00Z')
                ON CONFLICT ("Name") DO NOTHING;
                """);
        }

        private static void InsertExpenseType(MigrationBuilder migrationBuilder, string id, string name, string code)
        {
            migrationBuilder.Sql($"""
                INSERT INTO expense_types ("Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES ('{id}', '{name}', '{code}', TRUE, FALSE, TIMESTAMPTZ '2026-06-23T00:00:00Z', TIMESTAMPTZ '2026-06-23T00:00:00Z')
                ON CONFLICT ("Name") DO NOTHING;
                """);
        }
    }
}
