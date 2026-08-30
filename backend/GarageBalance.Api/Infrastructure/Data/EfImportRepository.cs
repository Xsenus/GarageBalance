using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfImportRepository(GarageBalanceDbContext dbContext) : IImportRepository
{
    public async Task<IReadOnlyList<AccessImportRunListItemData>> GetRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRuns.AsNoTracking();
        if (IsSqliteProvider())
        {
            return (await query
                    .Select(run => new AccessImportRunListItemData(
                        run.Id,
                        run.Status,
                        run.OriginalFileName,
                        run.StartedAtUtc,
                        run.FinishedAtUtc,
                        run.TotalChecks,
                        run.PassedChecks,
                        run.WarningCount,
                        run.ErrorCount,
                        run.Summary))
                    .ToListAsync(cancellationToken))
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .Take(limit)
                .ToList();
        }

        return await query
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Take(limit)
            .Select(run => new AccessImportRunListItemData(
                run.Id,
                run.Status,
                run.OriginalFileName,
                run.StartedAtUtc,
                run.FinishedAtUtc,
                run.TotalChecks,
                run.PassedChecks,
                run.WarningCount,
                run.ErrorCount,
                run.Summary))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> RunExistsAsync(Guid runId, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportRuns.AsNoTracking().AnyAsync(run => run.Id == runId, cancellationToken);
    }

    public Task<AccessImportRunStatusData?> FindRunStatusAsync(Guid runId, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportRuns
            .AsNoTracking()
            .Where(run => run.Id == runId)
            .Select(run => new AccessImportRunStatusData(
                run.Id,
                run.Status,
                run.FinishedAtUtc,
                run.TotalChecks,
                run.PassedChecks,
                run.WarningCount,
                run.ErrorCount,
                run.Summary))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AccessImportRunLogEntryListData> GetRunLogEntryListDataAsync(
        Guid runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRunLogEntries.AsNoTracking()
            .Where(entry => entry.AccessImportRunId == runId);
        if (dbContext.Database.IsNpgsql())
        {
            var orderedEntries = query
                .OrderBy(entry => entry.CreatedAtUtc)
                .ThenBy(entry => entry.Id)
                .Take(limit);
            var rows = await dbContext.AccessImportRuns
                .AsNoTracking()
                .Where(run => run.Id == runId)
                .SelectMany(
                    _ => orderedEntries.DefaultIfEmpty(),
                    (_, entry) => entry)
                .Select(entry => entry == null
                    ? null
                    : new AccessImportRunLogEntryListItemData(
                        entry.Id,
                        entry.AccessImportRunId,
                        entry.CreatedAtUtc,
                        entry.Level,
                        entry.StepCode,
                        entry.Message))
                .ToListAsync(cancellationToken);

            return new AccessImportRunLogEntryListData(
                rows.Count > 0,
                rows.Where(entry => entry is not null).Select(entry => entry!).ToList());
        }

        if (!await RunExistsAsync(runId, cancellationToken))
        {
            return new AccessImportRunLogEntryListData(false, []);
        }

        var entries = (await query
                .Select(entry => new AccessImportRunLogEntryListItemData(
                    entry.Id,
                    entry.AccessImportRunId,
                    entry.CreatedAtUtc,
                    entry.Level,
                    entry.StepCode,
                    entry.Message))
                .ToListAsync(cancellationToken))
            .OrderBy(entry => entry.CreatedAtUtc)
            .ThenBy(entry => entry.Id)
            .Take(limit)
            .ToList();
        return new AccessImportRunLogEntryListData(true, entries);
    }

    public async Task<AccessImportCreatedRecordListData> GetCreatedRecordListDataAsync(
        Guid runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportCreatedRecords.AsNoTracking()
            .Where(record => record.AccessImportRunId == runId);
        if (dbContext.Database.IsNpgsql())
        {
            var orderedRecords = query
                .OrderByDescending(record => record.CreatedAtUtc)
                .ThenBy(record => record.TargetEntityType)
                .ThenBy(record => record.TargetEntityId)
                .Take(limit);
            var rows = await dbContext.AccessImportRuns
                .AsNoTracking()
                .Where(run => run.Id == runId)
                .SelectMany(
                    _ => orderedRecords.DefaultIfEmpty(),
                    (_, record) => record)
                .ToListAsync(cancellationToken);

            return new AccessImportCreatedRecordListData(
                rows.Count > 0,
                rows.Where(record => record is not null).Select(record => record!).ToList());
        }

        if (!await RunExistsAsync(runId, cancellationToken))
        {
            return new AccessImportCreatedRecordListData(false, []);
        }

        var records = (await query.ToListAsync(cancellationToken))
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenBy(record => record.TargetEntityType, StringComparer.Ordinal)
            .ThenBy(record => record.TargetEntityId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        return new AccessImportCreatedRecordListData(true, records);
    }

    public Task<AccessImportRun?> FindRunAsync(
        Guid runId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRuns.AsQueryable();
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetQueuedRunIdsAsync(CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRuns
            .AsNoTracking()
            .Where(run => run.Status == "queued" || run.Status == "processing");
        if (IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .OrderBy(run => run.StartedAtUtc)
                .Take(1000)
                .Select(run => run.Id)
                .ToArray();
        }

        return await query
            .OrderBy(run => run.StartedAtUtc)
            .Take(1000)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<PreviousAccessImportRunData?> FindPreviousRunByContentAsync(
        string contentSha256,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRuns.AsNoTracking()
            .Where(run => run.ContentSha256 == contentSha256);
        if (IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .OrderByDescending(run => run.StartedAtUtc)
                .ThenByDescending(run => run.Id)
                .Select(run => new PreviousAccessImportRunData(run.Id, run.OriginalFileName, run.StartedAtUtc))
                .FirstOrDefault();
        }

        return await query
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenByDescending(run => run.Id)
            .Select(run => new PreviousAccessImportRunData(run.Id, run.OriginalFileName, run.StartedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AccessImportAuditData> GetAuditDataAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresAuditDataAsync(runId, cancellationToken);
        }

        var query = dbContext.AccessImportCreatedRecords.AsNoTracking()
            .Where(record => record.AccessImportRunId == runId);
        var counts = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                CreatedRecordCount = group.Count(),
                PendingRollbackRecordCount = group.Count(record => record.RollbackStatus == "created"),
                SourceRowFingerprintCount = group
                    .Where(record => record.SourceRowHash != string.Empty)
                    .Select(record => record.SourceRowHash)
                    .Distinct()
                    .Count()
            })
            .SingleOrDefaultAsync(cancellationToken);
        var targetEntityTypeSamples = query.Select(record => record.TargetEntityType)
            .Where(targetEntityType => targetEntityType != string.Empty)
            .Distinct()
            .OrderBy(targetEntityType => targetEntityType)
            .Take(10)
            .Select(value => new { Kind = 0, Value = value });
        var sourceRowFingerprintSamples = query
            .Select(record => record.SourceRowHash)
            .Where(rowHash => rowHash != string.Empty)
            .Distinct()
            .OrderBy(rowHash => rowHash)
            .Take(5)
            .Select(value => new { Kind = 1, Value = value });
        var samples = await targetEntityTypeSamples
            .Concat(sourceRowFingerprintSamples)
            .ToListAsync(cancellationToken);
        var targetEntityTypes = samples
            .Where(sample => sample.Kind == 0)
            .Select(sample => sample.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        var sourceRowFingerprints = samples
            .Where(sample => sample.Kind == 1)
            .Select(sample => sample.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        return new AccessImportAuditData(
            counts?.CreatedRecordCount ?? 0,
            counts?.PendingRollbackRecordCount ?? 0,
            counts?.SourceRowFingerprintCount ?? 0,
            targetEntityTypes,
            sourceRowFingerprints);
    }

    private async Task<AccessImportAuditData> GetPostgresAuditDataAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.SqlQuery<AccessImportAuditQueryRow>($$"""
            WITH records AS MATERIALIZED (
                SELECT
                    record."TargetEntityType",
                    record."SourceRowHash",
                    record."RollbackStatus"
                FROM access_import_created_records AS record
                WHERE record."AccessImportRunId" = {{runId}}
            ), counts AS (
                SELECT
                    COUNT(*)::int AS created_record_count,
                    COUNT(*) FILTER (WHERE "RollbackStatus" = 'created')::int AS pending_rollback_record_count,
                    COUNT(DISTINCT NULLIF("SourceRowHash", ''))::int AS source_row_fingerprint_count
                FROM records
            ), target_entity_type_samples AS (
                SELECT DISTINCT "TargetEntityType" AS value
                FROM records
                WHERE "TargetEntityType" <> ''
                ORDER BY value
                LIMIT 10
            ), source_row_fingerprint_samples AS (
                SELECT DISTINCT "SourceRowHash" AS value
                FROM records
                WHERE "SourceRowHash" <> ''
                ORDER BY value
                LIMIT 5
            )
            SELECT
                0 AS "Kind",
                NULL::text AS "Value",
                counts.created_record_count AS "CreatedRecordCount",
                counts.pending_rollback_record_count AS "PendingRollbackRecordCount",
                counts.source_row_fingerprint_count AS "SourceRowFingerprintCount"
            FROM counts

            UNION ALL

            SELECT 1, value, 0, 0, 0
            FROM target_entity_type_samples

            UNION ALL

            SELECT 2, value, 0, 0, 0
            FROM source_row_fingerprint_samples
            ORDER BY "Kind", "Value"
            """).ToListAsync(cancellationToken);

        var counts = rows.Single(row => row.Kind == 0);
        var targetEntityTypes = rows
            .Where(row => row.Kind == 1)
            .Select(row => row.Value!)
            .ToList();
        var sourceRowFingerprints = rows
            .Where(row => row.Kind == 2)
            .Select(row => row.Value!)
            .ToList();
        return new AccessImportAuditData(
            counts.CreatedRecordCount,
            counts.PendingRollbackRecordCount,
            counts.SourceRowFingerprintCount,
            targetEntityTypes,
            sourceRowFingerprints);
    }

    public void AddRun(AccessImportRun run) => dbContext.AccessImportRuns.Add(run);

    public void AddRunLogEntry(AccessImportRunLogEntry entry) => dbContext.AccessImportRunLogEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private bool IsSqliteProvider() =>
        string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);

    private sealed record AccessImportAuditQueryRow(
        int Kind,
        string? Value,
        int CreatedRecordCount,
        int PendingRollbackRecordCount,
        int SourceRowFingerprintCount);
}
