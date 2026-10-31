using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlDiscountAuditTests
{
    [PostgreSqlFact]
    public async Task AuditAcceptsIndependentCreditsAndStillDetectsInvalidSourcesLimitsAndGarages()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var penalty = await context.IncomeTypes.SingleAsync(item => item.Code == "penalty" && item.IsSystem);
        var garage = new Garage { Number = "AUDIT-CREDIT", PeopleCount = 1, FloorCount = 1 };
        var other = new Garage { Number = "AUDIT-OTHER", PeopleCount = 1, FloorCount = 1 };
        Accrual CreateAccrual(decimal amount) => new()
        {
            Garage = garage,
            IncomeType = penalty,
            Source = "manual",
            Amount = amount,
            AccountingMonth = new(2026, 7, 1),
            DueDate = new(2026, 8, 31),
            OverdueFromDate = new(2026, 10, 1)
        };
        var target = CreateAccrual(100);
        var first = CreateAccrual(-30);
        var second = CreateAccrual(-40);
        context.AddRange(garage, other, target, first, second,
            new AccrualPaymentAllocation { Accrual = target, CreditAccrual = first, Amount = 30 },
            new AccrualPaymentAllocation { Accrual = target, CreditAccrual = second, Amount = 40 });
        await context.SaveChangesAsync();
        foreach (var name in new[] { "invalid_accrual_amounts", "duplicate_active_allocations", "invalid_allocation_sources", "allocations_to_invalid_credits", "allocation_totals_above_credit", "allocation_totals_above_accrual" })
            Assert.Equal(0, await FindingsAsync(database.ConnectionString, name));

        first.Amount = -10;
        await context.SaveChangesAsync();
        Assert.Equal(1, await FindingsAsync(database.ConnectionString, "allocation_totals_above_credit"));
        first.Amount = -30;
        first.Garage = other;
        await context.SaveChangesAsync();
        Assert.Equal(1, await FindingsAsync(database.ConnectionString, "allocations_to_invalid_credits"));
        first.Garage = garage;
        first.Source = "regular";
        await context.SaveChangesAsync();
        Assert.Equal(1, await FindingsAsync(database.ConnectionString, "invalid_accrual_amounts"));
        Assert.Equal(1, await FindingsAsync(database.ConnectionString, "allocations_to_invalid_credits"));
        first.Source = "manual";
        first.IsCanceled = true;
        await context.SaveChangesAsync();
        Assert.Equal(1, await FindingsAsync(database.ConnectionString, "allocations_to_invalid_credits"));
    }

    [PostgreSqlFact]
    public async Task AuditKeepsTheLegacyAllocationQueryExecutableBeforeCreditMigration()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync("20260831014500_OptimizeMeterReadingYearGrid");
        Assert.Equal(0, await FindingsAsync(database.ConnectionString, "duplicate_active_allocations", legacy: true));
        Assert.Equal(0, await FindingsAsync(database.ConnectionString, "invalid_accrual_amounts"));
    }

    private static async Task<long> FindingsAsync(string connectionString, string name, bool legacy = false)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var lines = File.ReadAllLines(Path.Combine(directory.FullName, "infrastructure", "scripts", "audit-staging-database.sh"));
        var starts = lines.Select((line, index) => (line, index))
            .Where(item => item.line.TrimStart().StartsWith($"run_check \"{name}\" \"critical\" \\", StringComparison.Ordinal)).ToArray();
        var start = legacy ? starts.Last().index : starts.First().index;
        var queryLine = lines[start + 1].Trim();
        Assert.StartsWith("\"", queryLine);
        Assert.EndsWith("\"", queryLine);
        var query = queryLine[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(query, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
