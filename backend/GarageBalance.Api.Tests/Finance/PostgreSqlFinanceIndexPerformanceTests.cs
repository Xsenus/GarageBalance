using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Npgsql;

namespace GarageBalance.Api.Tests.Finance;

public sealed class PostgreSqlFinanceIndexPerformanceTests
{
    [PostgreSqlFact]
    public async Task FinanceAndFundPredicatesUsePurposeBuiltIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var indexes = await ReadIndexesAsync(connection);
        AssertIndex(indexes, "IX_financial_operations_OperationKind_OperationDate_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~", "income");
        AssertIndex(indexes, "IX_accruals_AccountingMonth_GarageId_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc", "\"DueDateNeedsReview\" = false");
        AssertIndex(indexes, "IX_accrual_payment_allocations_AccrualId_FinancialOperationId", "\"IsActive\" = true");
        AssertIndex(indexes, "IX_supplier_accruals_AccountingMonth_SupplierId_Id", "\"IsCanceled\" = false");
        AssertIndex(indexes, "IX_fund_operations_FundId_CreatedAtUtc_Id", "\"FundId\"");

        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_OperationKind_OperationDate_Id",
            """
            SELECT "Id" FROM financial_operations
            WHERE "IsCanceled" = false
              AND "OperationKind" = 'expense'
              AND "OperationDate" BETWEEN DATE '2026-01-01' AND DATE '2026-12-31'
            ORDER BY "OperationDate" DESC, "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_financial_operations_GarageId_IncomeTypeId_OperationDate_Cr~",
            """
            SELECT "Id" FROM financial_operations
            WHERE "IsCanceled" = false
              AND "OperationKind" = 'income'
              AND "GarageId" = '00000000-0000-0000-0000-000000000001'
              AND "IncomeTypeId" = '00000000-0000-0000-0000-000000000002'
            ORDER BY "OperationDate", "CreatedAtUtc";
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_AccountingMonth_GarageId_Id",
            """
            SELECT "Id" FROM accruals
            WHERE "IsCanceled" = false
              AND "AccountingMonth" BETWEEN DATE '2026-01-01' AND DATE '2026-12-01'
            ORDER BY "AccountingMonth" DESC, "GarageId", "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accruals_GarageId_IncomeTypeId_DueDate_CreatedAtUtc",
            """
            SELECT "Id" FROM accruals
            WHERE "IsCanceled" = false
              AND "DueDateNeedsReview" = false
              AND "GarageId" = '00000000-0000-0000-0000-000000000001'
              AND "IncomeTypeId" = '00000000-0000-0000-0000-000000000002'
            ORDER BY "DueDate", "CreatedAtUtc";
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_accrual_payment_allocations_AccrualId_FinancialOperationId",
            """
            SELECT "FinancialOperationId" FROM accrual_payment_allocations
            WHERE "IsActive" = true
              AND "AccrualId" = '00000000-0000-0000-0000-000000000003';
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_supplier_accruals_AccountingMonth_SupplierId_Id",
            """
            SELECT "Id" FROM supplier_accruals
            WHERE "IsCanceled" = false
              AND "AccountingMonth" BETWEEN DATE '2026-01-01' AND DATE '2026-12-01'
            ORDER BY "AccountingMonth" DESC, "SupplierId", "Id"
            LIMIT 25;
            """);
        await AssertPlanUsesAsync(
            connection,
            "IX_fund_operations_FundId_CreatedAtUtc_Id",
            """
            SELECT "Id" FROM fund_operations
            WHERE "FundId" = '00000000-0000-0000-0000-000000000004'
              AND "CreatedAtUtc" >= TIMESTAMPTZ '2026-01-01T00:00:00Z'
            ORDER BY "CreatedAtUtc", "Id";
            """);
    }

    [PostgreSqlFact]
    public async Task AllocationAndFundTailQueriesHonorCancellation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var allocationRepository = new EfAccrualPaymentAllocationRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            allocationRepository.RebuildAsync(
                [new AccrualPaymentAllocationKey(Guid.NewGuid(), Guid.NewGuid())],
                cancellation.Token));

        var fundRepository = new EfFundRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fundRepository.GetOperationsFromAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fundRepository.GetOperationsSinceAsync(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                cancellation.Token));
    }

    private static async Task<Dictionary<string, string>> ReadIndexesAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename IN (
                'financial_operations',
                'accruals',
                'accrual_payment_allocations',
                'supplier_accruals',
                'fund_operations');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(0)] = reader.GetString(1);
        }

        return indexes;
    }

    private static void AssertIndex(
        IReadOnlyDictionary<string, string> indexes,
        string name,
        string expectedDefinition)
    {
        Assert.True(indexes.TryGetValue(name, out var definition), $"Index {name} was not created.");
        Assert.Contains(expectedDefinition, definition, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertPlanUsesAsync(
        NpgsqlConnection connection,
        string indexName,
        string query)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET enable_seqscan = off; EXPLAIN (ANALYZE, BUFFERS) {query}";
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        Assert.Contains(indexName, string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
    }
}
