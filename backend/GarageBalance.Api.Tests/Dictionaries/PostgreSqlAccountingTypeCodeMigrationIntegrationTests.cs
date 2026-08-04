using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PostgreSqlAccountingTypeCodeMigrationIntegrationTests
{
    private const string PreviousMigration = "20260804045550_RemoveLegacyFormStates";

    [PostgreSqlFact]
    public async Task Migration_NormalizesLegacyCodesResolvesConflictsAndAuditsRepair()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var systemIncomeId = Guid.NewGuid();
        var duplicateIncomeId = Guid.NewGuid();
        var invalidIncomeId = Guid.NewGuid();
        var normalizedIncomeId = Guid.NewGuid();
        var systemExpenseId = Guid.NewGuid();
        var duplicateExpenseId = Guid.NewGuid();

        await using (var setupContext = database.CreateContext())
        {
            await setupContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await setupContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO income_types (
                    "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({systemIncomeId}, 'Системная вода', ' WATER ', TRUE, FALSE, CURRENT_TIMESTAMP - INTERVAL '2 days', CURRENT_TIMESTAMP),
                    ({duplicateIncomeId}, 'Пользовательская вода', 'water', FALSE, FALSE, CURRENT_TIMESTAMP - INTERVAL '1 day', CURRENT_TIMESTAMP),
                    ({invalidIncomeId}, 'Некорректный код', 'код-1', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                    ({normalizedIncomeId}, 'Охрана', ' SECURITY_2026 ', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

                INSERT INTO expense_types (
                    "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    ({systemExpenseId}, 'Системная электроэнергия', ' ELECTRICITY ', TRUE, FALSE, CURRENT_TIMESTAMP - INTERVAL '2 days', CURRENT_TIMESTAMP),
                    ({duplicateExpenseId}, 'Пользовательская электроэнергия', 'electricity', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
                """);

            await setupContext.Database.MigrateAsync();
        }

        await using var verificationContext = database.CreateContext();
        Assert.Single(await verificationContext.IncomeTypes.Where(item => !item.IsArchived && item.Code == "water" && item.IsSystem).ToListAsync());
        Assert.Null(await verificationContext.IncomeTypes.Where(item => item.Id == duplicateIncomeId).Select(item => item.Code).SingleAsync());
        Assert.Null(await verificationContext.IncomeTypes.Where(item => item.Id == invalidIncomeId).Select(item => item.Code).SingleAsync());
        Assert.Equal("security_2026", await verificationContext.IncomeTypes.Where(item => item.Id == normalizedIncomeId).Select(item => item.Code).SingleAsync());
        Assert.Single(await verificationContext.ExpenseTypes.Where(item => !item.IsArchived && item.Code == "electricity" && item.IsSystem).ToListAsync());
        Assert.Null(await verificationContext.ExpenseTypes.Where(item => item.Id == duplicateExpenseId).Select(item => item.Code).SingleAsync());
        Assert.Single(await verificationContext.AuditEvents
            .Where(item => item.Action == "dictionary.accounting_type_codes_repaired")
            .ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Database_RejectsDuplicateInvalidAndCustomReservedCodes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO income_types (
                "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ('6c39e25a-acde-4eed-af6a-a105eb68768f', 'Охрана', 'security_2026', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO income_types (
                "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ('672111b5-bf65-4dc7-a7ae-43e7358f4f80', 'Охрана территории', 'security_2026', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);

        var invalid = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO expense_types (
                "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ('50ea3272-d349-4faa-8c69-52d40aafc147', 'Неверная статья', 'repair-item', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);

        var reserved = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO expense_types (
                "Id", "Name", "Code", "IsSystem", "IsArchived", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES
                ('6710a890-e597-4909-85a7-f2c2aed089b9', 'Пользовательская зарплата', 'salary', FALSE, FALSE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, reserved.SqlState);
    }
}
