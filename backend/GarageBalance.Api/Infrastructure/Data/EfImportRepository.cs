using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfImportRepository(GarageBalanceDbContext dbContext) : IImportRepository
{
    public async Task<IReadOnlyList<AccessImportRun>> GetRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRuns.AsNoTracking();
        return IsSqliteProvider()
            ? (await query.ToListAsync(cancellationToken)).OrderByDescending(run => run.StartedAtUtc).ThenByDescending(run => run.Id).Take(limit).ToList()
            : await query.OrderByDescending(run => run.StartedAtUtc).ThenByDescending(run => run.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<bool> RunExistsAsync(Guid runId, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportRuns.AsNoTracking().AnyAsync(run => run.Id == runId, cancellationToken);
    }

    public async Task<IReadOnlyList<AccessImportRunLogEntry>> GetRunLogEntriesAsync(
        Guid runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportRunLogEntries.AsNoTracking()
            .Where(entry => entry.AccessImportRunId == runId);
        return IsSqliteProvider()
            ? (await query.ToListAsync(cancellationToken)).OrderBy(entry => entry.CreatedAtUtc).ThenBy(entry => entry.Id).Take(limit).ToList()
            : await query.OrderBy(entry => entry.CreatedAtUtc).ThenBy(entry => entry.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccessImportCreatedRecord>> GetCreatedRecordsAsync(
        Guid runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportCreatedRecords.AsNoTracking()
            .Where(record => record.AccessImportRunId == runId);
        if (dbContext.Database.IsNpgsql())
        {
            return await query
                .OrderByDescending(record => record.CreatedAtUtc)
                .ThenBy(record => record.TargetEntityType)
                .ThenBy(record => record.TargetEntityId)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        return (await query.ToListAsync(cancellationToken))
            .OrderByDescending(record => record.CreatedAtUtc)
            .ThenBy(record => record.TargetEntityType, StringComparer.Ordinal)
            .ThenBy(record => record.TargetEntityId, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
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
        var query = dbContext.AccessImportCreatedRecords.AsNoTracking()
            .Where(record => record.AccessImportRunId == runId);
        var createdRecordCount = await query.CountAsync(cancellationToken);
        var pendingRollbackRecordCount = await query.CountAsync(record => record.RollbackStatus == "created", cancellationToken);
        var sourceRowFingerprintCount = await query.Select(record => record.SourceRowHash)
            .Where(rowHash => rowHash != string.Empty)
            .Distinct()
            .CountAsync(cancellationToken);
        var targetEntityTypes = await query.Select(record => record.TargetEntityType)
            .Where(targetEntityType => targetEntityType != string.Empty)
            .Distinct()
            .OrderBy(targetEntityType => targetEntityType)
            .Take(10)
            .ToListAsync(cancellationToken);
        var sourceRowFingerprints = (await query
                .OrderBy(record => record.TargetEntityType)
                .ThenBy(record => record.TargetEntityId)
                .Select(record => record.SourceRowHash)
                .Where(rowHash => rowHash != string.Empty)
                .Take(20)
                .ToListAsync(cancellationToken))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();
        return new AccessImportAuditData(
            createdRecordCount,
            pendingRollbackRecordCount,
            sourceRowFingerprintCount,
            targetEntityTypes,
            sourceRowFingerprints);
    }

    public void AddRun(AccessImportRun run) => dbContext.AccessImportRuns.Add(run);

    public void AddRunLogEntry(AccessImportRunLogEntry entry) => dbContext.AccessImportRunLogEntries.Add(entry);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private bool IsSqliteProvider() =>
        string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal);
}
