using System.Data.Common;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Infrastructure.Data;
using GarageBalance.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GarageBalance.Api.Tests.Import;

public sealed class PostgreSqlImportPerformanceTests
{
    [PostgreSqlFact]
    public async Task ImportAuditCombinesAllCountersAndKeepsSamplesBounded()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var runId = Guid.NewGuid();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportCreatedRecords.AddRange(
                CreateRecord(runId, "garage", "garage-2", new string('b', 64), "created", 2),
                CreateRecord(runId, "financial_operation", "operation-1", new string('a', 64), "rolled_back", 1),
                CreateRecord(runId, "garage", "garage-1", new string('a', 64), "created", 0),
                CreateRecord(runId, "", "ignored-empty-type", string.Empty, "created", 3),
                CreateRecord(Guid.NewGuid(), "other_run", "ignored-run", new string('c', 64), "created", 4));
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context).GetAuditDataAsync(runId, CancellationToken.None);

        Assert.Equal(4, result.CreatedRecordCount);
        Assert.Equal(3, result.PendingRollbackRecordCount);
        Assert.Equal(2, result.SourceRowFingerprintCount);
        Assert.Equal(["financial_operation", "garage"], result.TargetEntityTypes);
        Assert.Equal([new string('a', 64), new string('b', 64)], result.SourceRowFingerprints);
        Assert.Equal(3, commandCapture.Commands.Count);
        Assert.Contains("COUNT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.All(commandCapture.Commands, command => Assert.Contains("AccessImportRunId", command, StringComparison.Ordinal));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task ImportAuditReturnsFiveDistinctFingerprintsWhenEarlyRowsRepeat()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var runId = Guid.NewGuid();
        var repeatedHash = new string('a', 64);
        var records = Enumerable.Range(0, 25)
            .Select(index => CreateRecord(
                runId,
                "duplicate",
                $"duplicate-{index:D2}",
                repeatedHash,
                "created",
                index))
            .Concat(Enumerable.Range(0, 5).Select(index => CreateRecord(
                runId,
                "unique",
                $"unique-{index:D2}",
                new string((char)('b' + index), 64),
                "created",
                25 + index)))
            .ToArray();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportCreatedRecords.AddRange(records);
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context).GetAuditDataAsync(runId, CancellationToken.None);

        Assert.Equal(30, result.CreatedRecordCount);
        Assert.Equal(6, result.SourceRowFingerprintCount);
        Assert.Equal(
            Enumerable.Range(0, 5).Select(index => new string((char)('a' + index), 64)),
            result.SourceRowFingerprints);
        Assert.Equal(3, commandCapture.Commands.Count);
        Assert.Contains("DISTINCT", commandCapture.Commands[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", commandCapture.Commands[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", commandCapture.Commands[2], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TargetEntityId", commandCapture.Commands[2], StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task ImportAuditReturnsEmptyResultInThreeSelects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context).GetAuditDataAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.CreatedRecordCount);
        Assert.Equal(0, result.PendingRollbackRecordCount);
        Assert.Equal(0, result.SourceRowFingerprintCount);
        Assert.Empty(result.TargetEntityTypes);
        Assert.Empty(result.SourceRowFingerprints);
        Assert.Equal(3, commandCapture.Commands.Count);
        Assert.Empty(context.ChangeTracker.Entries());
    }

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

    private static AccessImportCreatedRecord CreateRecord(
        Guid runId,
        string targetEntityType,
        string targetEntityId,
        string sourceRowHash,
        string rollbackStatus,
        int minuteOffset) =>
        new()
        {
            AccessImportRunId = runId,
            SourceEntityType = "Garage",
            SourceExternalId = targetEntityId,
            SourceRowHash = sourceRowHash,
            TargetEntityType = targetEntityType,
            TargetEntityId = targetEntityId,
            RollbackStatus = rollbackStatus,
            CreatedAtUtc = new DateTimeOffset(2026, 8, 30, 1, minuteOffset, 0, TimeSpan.Zero)
        };

    private sealed class SelectCommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Commands.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
