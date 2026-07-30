using GarageBalance.Api.Tests.Common;
using Npgsql;

namespace GarageBalance.Api.Tests.Import;

public sealed class PostgreSqlImportPerformanceTests
{
    [PostgreSqlFact]
    public async Task ImportRecoveryDuplicateAndQuarantineQueriesUseCompositeIndexes()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND tablename IN ('access_import_runs', 'access_import_row_fingerprints', 'access_import_quarantine_items', 'app_releases');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes[reader.GetString(0)] = reader.GetString(1);
            }
        }

        Assert.Contains("IX_access_import_runs_Status_StartedAtUtc", indexes.Keys);
        Assert.Contains("IX_access_import_runs_ContentSha256_StartedAtUtc", indexes.Keys);
        Assert.Contains("IX_access_import_row_fingerprints_FingerprintKey", indexes.Keys);
        Assert.Contains("IX_access_import_quarantine_items_Status_CreatedAtUtc_Id", indexes.Keys);
        Assert.Contains("IX_access_import_quarantine_items_Status_AccessImportRunId_Cre~", indexes.Keys);
        Assert.Contains("IX_app_releases_IsPublished_PublishedAt", indexes.Keys);

        await using (var seedCommand = connection.CreateCommand())
        {
            seedCommand.CommandText =
                """
                INSERT INTO access_import_quarantine_items
                    ("Id", "AccessImportRunId", "SourceSystem", "EntityType", "ExternalId",
                     "RowHash", "ReasonCode", "ReasonMessage", "Severity", "RowSnapshotJson",
                     "Status", "CreatedAtUtc")
                SELECT md5(i::text)::uuid,
                       md5((i % 50)::text)::uuid,
                       'Access',
                       'Garage',
                       i::text,
                       repeat('a', 64),
                       'invalid',
                       'Invalid',
                       'error',
                       '{}'::jsonb,
                       CASE WHEN i % 3 = 0 THEN 'resolved' ELSE 'open' END,
                       TIMESTAMPTZ '2026-01-01' + i * INTERVAL '1 minute'
                FROM generate_series(1, 5000) i;

                ANALYZE access_import_quarantine_items;
                """;
            await seedCommand.ExecuteNonQueryAsync();
        }

        await AssertPlanUsesIndexAsync(
            connection,
            """
            SELECT "Id" FROM access_import_runs
            WHERE "Status" IN ('queued', 'processing')
            ORDER BY "StartedAtUtc"
            LIMIT 25;
            """,
            "IX_access_import_runs_Status_StartedAtUtc");
        await AssertPlanUsesIndexAsync(
            connection,
            """
            SELECT "Id" FROM access_import_runs
            WHERE "ContentSha256" = repeat('a', 64)
            ORDER BY "StartedAtUtc" DESC
            LIMIT 1;
            """,
            "IX_access_import_runs_ContentSha256_StartedAtUtc");
        await AssertPlanUsesIndexAsync(
            connection,
            """
            SELECT "Id" FROM access_import_quarantine_items
            WHERE "Status" = 'open'
              AND "AccessImportRunId" = '00000000-0000-0000-0000-000000000001'
            ORDER BY "CreatedAtUtc" DESC, "Id" DESC
            LIMIT 50;
            """,
            "IX_access_import_quarantine_items_Status_AccessImportRunId_Cre~");
    }

    private static async Task AssertPlanUsesIndexAsync(
        NpgsqlConnection connection,
        string sql,
        string expectedIndex)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET enable_seqscan = off; EXPLAIN (FORMAT TEXT) {sql}";
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        Assert.Contains(expectedIndex, string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
    }
}
