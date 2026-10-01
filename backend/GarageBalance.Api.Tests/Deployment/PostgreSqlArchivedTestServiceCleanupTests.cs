using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Domain.Settings;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlArchivedTestServiceCleanupTests
{
    [PostgreSqlFact]
    public async Task ExplicitArchivedCleanupAuditsAndIsIdempotent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var service = new ChargeServiceSetting { Name = "Удаляемая тестовая услуга", IsArchived = true };
        context.Add(service);
        await context.SaveChangesAsync();
        await RunAsync(database.ConnectionString, service.Id.ToString(), execute: true);
        Assert.False(await context.ChargeServiceSettings.AsNoTracking().AnyAsync(row => row.Id == service.Id));
        var audit = await context.AuditEvents.AsNoTracking().SingleAsync(row => row.EntityId == service.Id.ToString());
        Assert.Equal("delete", audit.ActionKind);
        Assert.Equal("dictionary", audit.Section);
        Assert.Contains("Проверочная очистка", audit.MetadataJson);
        await RunAsync(database.ConnectionString, service.Id.ToString(), execute: true);
        Assert.Equal(1, await context.AuditEvents.CountAsync(row => row.EntityId == service.Id.ToString()));
    }

    [PostgreSqlFact]
    public async Task DryRunRollsBackBothDeletionAndAudit()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var service = new ChargeServiceSetting { Name = "Услуга пробной очистки", IsArchived = true };
        context.Add(service);
        await context.SaveChangesAsync();
        await RunAsync(database.ConnectionString, service.Id.ToString(), execute: false);
        Assert.True(await context.ChargeServiceSettings.AsNoTracking().AnyAsync(row => row.Id == service.Id));
        Assert.False(await context.AuditEvents.AnyAsync(row => row.EntityId == service.Id.ToString()));
    }

    [PostgreSqlFact]
    public async Task ActiveReferencedAndPartiallyMissingTargetsAreRejectedWithoutWrites()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var active = new ChargeServiceSetting { Name = "Рабочая услуга" };
        var referenced = new ChargeServiceSetting { Name = "Архивная привязанная услуга", IsArchived = true };
        var group = new SupplierGroup { Name = "Проверочная группа" };
        context.AddRange(active, referenced, group, new Supplier { Name = "Проверочный поставщик", GroupId = group.Id, ChargeServiceSettingId = referenced.Id });
        await context.SaveChangesAsync();
        foreach (var ids in new[] { active.Id.ToString(), referenced.Id.ToString(), referenced.Id + "," + Guid.NewGuid() })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString, ids, execute: true));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        }
        Assert.Equal(2, await context.ChargeServiceSettings.CountAsync(row => row.Id == active.Id || row.Id == referenced.Id));
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Action == "dictionary.charge_service_test_purged"));
    }

    [PostgreSqlFact]
    public async Task FinancialAndReportReferencesRejectTheWholeBatchWithoutDeletingSafeService()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var income = new IncomeType { Name = "Проверочный вид поступления" };
        var financial = new ChargeServiceSetting { Name = "Связана с платежом", IsArchived = true, IncomeTypeId = income.Id };
        var report = new ChargeServiceSetting { Name = "Связана с отчётом", IsArchived = true };
        var safe = new ChargeServiceSetting { Name = "Без связей", IsArchived = true };
        context.AddRange(income, financial, report, safe,
            new FinancialOperation
            {
                OperationKind = FinancialOperationKinds.Income,
                IncomeTypeId = income.Id,
                Amount = 123m,
                OperationDate = new DateOnly(2026, 10, 1),
                AccountingMonth = new DateOnly(2026, 10, 1)
            },
            new ApplicationSetting { Key = "cleanup-test-report", JsonValue = $"{{\"serviceId\":\"{report.Id}\"}}" });
        await context.SaveChangesAsync();
        foreach (var referenced in new[] { financial, report })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString,
                safe.Id + "," + referenced.Id, execute: true));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        }
        Assert.Equal(3, await context.ChargeServiceSettings.CountAsync(row => row.Id == financial.Id || row.Id == report.Id || row.Id == safe.Id));
        Assert.Equal(123m, await context.FinancialOperations.Where(row => row.IncomeTypeId == income.Id).SumAsync(row => row.Amount));
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Action == "dictionary.charge_service_test_purged"));
    }

    [PostgreSqlFact]
    public async Task WrongDatabaseEmptyDuplicateIdsAndMissingReasonAreRejected()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var id = Guid.NewGuid().ToString();
        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString, id, true, expectedDatabase: "garagebalance_staging"));
        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString, "", true));
        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString, id + "," + id, true));
        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(database.ConnectionString, id, true, reason: ""));
    }

    private static async Task RunAsync(string connectionString, string ids, bool execute,
        string? expectedDatabase = null, string reason = "Проверочная очистка")
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "infrastructure", "scripts", "purge-archived-test-services.sql"));
        var body = script[script.IndexOf("DO $cleanup$", StringComparison.Ordinal)..script.IndexOf("\\if :execute", StringComparison.Ordinal)];
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("BEGIN; SET LOCAL lock_timeout='5s'; "
            + "SELECT set_config('garagebalance.cleanup_database', @database, true), "
            + "set_config('garagebalance.cleanup_ids', @ids, true), "
            + "set_config('garagebalance.cleanup_reason', @reason, true); "
            + body + (execute ? "COMMIT;" : "ROLLBACK;"), connection);
        command.Parameters.AddWithValue("database", expectedDatabase ?? connection.Database);
        command.Parameters.AddWithValue("ids", ids);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync();
    }
}
