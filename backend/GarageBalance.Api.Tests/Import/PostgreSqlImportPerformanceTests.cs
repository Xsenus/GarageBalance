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
    public async Task OpenQuarantineListProjectsOnlyBoundedPublicColumnsInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun { OriginalFileName = "quarantine.accdb" };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            seedContext.AccessImportQuarantineItems.AddRange(Enumerable.Range(1, 60).Select(index => new AccessImportQuarantineItem
            {
                AccessImportRunId = run.Id,
                SourceSystem = "Access",
                EntityType = "Garage",
                ExternalId = $"garage-{index:D2}",
                RowHash = index.ToString("x64"),
                ReasonCode = "missing-owner",
                ReasonMessage = "Не найден владелец гаража.",
                RowSnapshotJson = $"{{\"payload\":\"{new string('x', 20_000)}\"}}",
                CreatedAtUtc = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero).AddMinutes(index)
            }));
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportQuarantineRepository(context)
            .GetOpenItemsAsync(run.Id, 50, CancellationToken.None);

        Assert.Equal(50, result.Count);
        Assert.Equal("garage-60", result[0].ExternalId);
        Assert.DoesNotContain(result, item => item.ExternalId == "garage-01");
        var command = Assert.Single(commandCapture.Commands);
        Assert.Contains("AccessImportRunId", command, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowSnapshotJson", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RunListProjectsOnlyBoundedSummaryColumnsInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.AddRange(Enumerable.Range(1, 60).Select(index => new AccessImportRun
            {
                OriginalFileName = $"history-{index:D2}.accdb",
                StartedAtUtc = new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero).AddMinutes(index),
                ContentSha256 = new string((char)('a' + index % 20), 64),
                ReportJson = $"[{{\"message\":\"{new string('x', 20_000)}\"}}]"
            }));
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context).GetRunsAsync(50, CancellationToken.None);

        Assert.Equal(50, result.Count);
        Assert.Equal("history-60.accdb", result[0].OriginalFileName);
        Assert.DoesNotContain(result, run => run.OriginalFileName == "history-01.accdb");
        var command = Assert.Single(commandCapture.Commands);
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReportJson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentSha256", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FileExtension", command, StringComparison.Ordinal);
        Assert.DoesNotContain("FileSizeBytes", command, StringComparison.Ordinal);
        Assert.DoesNotContain("Mode", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task ExactRunLookupLoadsOnlyRequestedRunInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var requestedRun = new AccessImportRun
        {
            OriginalFileName = "requested.accdb",
            ReportJson = "[{\"code\":\"requested\",\"title\":\"Requested\",\"status\":\"passed\",\"message\":\"Ready\"}]"
        };
        var otherRun = new AccessImportRun
        {
            OriginalFileName = "other.accdb",
            ReportJson = "[{\"code\":\"other\",\"title\":\"Other\",\"status\":\"passed\",\"message\":\"Ignore\"}]"
        };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.AddRange(requestedRun, otherRun);
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .FindRunAsync(requestedRun.Id, false, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("requested.accdb", result.OriginalFileName);
        Assert.Single(commandCapture.Commands);
        Assert.Contains("WHERE", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_import_runs", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RunStatusLookupProjectsOnlyLightweightColumnsInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun
        {
            Status = "processing",
            OriginalFileName = "large-status.accdb",
            ContentSha256 = new string('a', 64),
            Summary = "Фоновая проверка выполняется.",
            ReportJson = $"[{{\"message\":\"{new string('x', 20_000)}\"}}]"
        };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .FindRunStatusAsync(run.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("processing", result.Status);
        Assert.Equal("Фоновая проверка выполняется.", result.Summary);
        var command = Assert.Single(commandCapture.Commands);
        Assert.Contains("access_import_runs", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReportJson", command, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentSha256", command, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalFileName", command, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RunLogListLoadsRunAndBoundedEntriesInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun { OriginalFileName = "run-log.accdb" };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            seedContext.AccessImportRunLogEntries.AddRange(
                CreateLogEntry(run.Id, "third", 3),
                CreateLogEntry(run.Id, "first", 1),
                CreateLogEntry(run.Id, "second", 2));
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetRunLogEntryListDataAsync(run.Id, 2, CancellationToken.None);

        Assert.True(result.RunExists);
        Assert.Equal(["first", "second"], result.Entries.Select(entry => entry.StepCode));
        Assert.Single(commandCapture.Commands);
        Assert.Contains("JOIN", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_import_runs", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_import_run_log_entries", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DetailsJson", commandCapture.Commands[0], StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RunLogListDistinguishesEmptyRunInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun { OriginalFileName = "empty-log.accdb" };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetRunLogEntryListDataAsync(run.Id, 100, CancellationToken.None);

        Assert.True(result.RunExists);
        Assert.Empty(result.Entries);
        Assert.Single(commandCapture.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task RunLogListReturnsMissingRunInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetRunLogEntryListDataAsync(Guid.NewGuid(), 100, CancellationToken.None);

        Assert.False(result.RunExists);
        Assert.Empty(result.Entries);
        Assert.Single(commandCapture.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CreatedRecordListLoadsRunAndBoundedRecordsInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun { OriginalFileName = "created-records.accdb" };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            seedContext.AccessImportCreatedRecords.AddRange(
                CreateRecord(run.Id, "garage", "garage-1", new string('a', 64), "created", 1),
                CreateRecord(run.Id, "garage", "garage-3", new string('c', 64), "created", 3),
                CreateRecord(run.Id, "garage", "garage-2", new string('b', 64), "created", 2));
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetCreatedRecordListDataAsync(run.Id, 2, CancellationToken.None);

        Assert.True(result.RunExists);
        Assert.Equal(["garage-3", "garage-2"], result.Records.Select(record => record.TargetEntityId));
        Assert.Single(commandCapture.Commands);
        Assert.Contains("JOIN", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_import_runs", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("access_import_created_records", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CreatedRecordListDistinguishesEmptyRunInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var run = new AccessImportRun { OriginalFileName = "empty.accdb" };
        await using (var seedContext = database.CreateContext())
        {
            seedContext.AccessImportRuns.Add(run);
            await seedContext.SaveChangesAsync();
        }

        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetCreatedRecordListDataAsync(run.Id, 100, CancellationToken.None);

        Assert.True(result.RunExists);
        Assert.Empty(result.Records);
        Assert.Single(commandCapture.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [PostgreSqlFact]
    public async Task CreatedRecordListReturnsMissingRunInOneSelect()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        var commandCapture = new SelectCommandCapture();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(commandCapture)
            .Options;
        await using var context = new GarageBalanceDbContext(options);

        var result = await new EfImportRepository(context)
            .GetCreatedRecordListDataAsync(Guid.NewGuid(), 100, CancellationToken.None);

        Assert.False(result.RunExists);
        Assert.Empty(result.Records);
        Assert.Single(commandCapture.Commands);
        Assert.Empty(context.ChangeTracker.Entries());
    }

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
        Assert.Equal(2, commandCapture.Commands.Count);
        Assert.Contains("COUNT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCT", commandCapture.Commands[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNION ALL", commandCapture.Commands[1], StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal(2, commandCapture.Commands.Count);
        Assert.Contains("UNION ALL", commandCapture.Commands[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DISTINCT", commandCapture.Commands[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", commandCapture.Commands[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", commandCapture.Commands[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TargetEntityId", commandCapture.Commands[1], StringComparison.Ordinal);
    }

    [PostgreSqlFact]
    public async Task ImportAuditReturnsEmptyResultInTwoSelects()
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
        Assert.Equal(2, commandCapture.Commands.Count);
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

    private static AccessImportRunLogEntry CreateLogEntry(Guid runId, string stepCode, int minuteOffset) =>
        new()
        {
            AccessImportRunId = runId,
            StepCode = stepCode,
            Message = stepCode,
            DetailsJson = $"{{\"payload\":\"{new string('x', 20_000)}\"}}",
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
