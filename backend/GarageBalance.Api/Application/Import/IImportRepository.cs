using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportRepository
{
    Task<IReadOnlyList<AccessImportRun>> GetRunsAsync(int limit, CancellationToken cancellationToken);
    Task<bool> RunExistsAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessImportRunLogEntry>> GetRunLogEntriesAsync(Guid runId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessImportCreatedRecord>> GetCreatedRecordsAsync(Guid runId, int limit, CancellationToken cancellationToken);
    Task<AccessImportRun?> FindRunAsync(Guid runId, bool trackChanges, CancellationToken cancellationToken);
    Task<PreviousAccessImportRunData?> FindPreviousRunByContentAsync(string contentSha256, CancellationToken cancellationToken);
    Task<AccessImportAuditData> GetAuditDataAsync(Guid runId, CancellationToken cancellationToken);
    void AddRun(AccessImportRun run);
    void AddRunLogEntry(AccessImportRunLogEntry entry);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PreviousAccessImportRunData(
    Guid Id,
    string OriginalFileName,
    DateTimeOffset StartedAtUtc);

public sealed record AccessImportAuditData(
    int CreatedRecordCount,
    int PendingRollbackRecordCount,
    int SourceRowFingerprintCount,
    IReadOnlyList<string> TargetEntityTypes,
    IReadOnlyList<string> SourceRowFingerprints);
