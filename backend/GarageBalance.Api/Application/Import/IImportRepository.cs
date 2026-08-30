using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportRepository
{
    Task<IReadOnlyList<AccessImportRunListItemData>> GetRunsAsync(int limit, CancellationToken cancellationToken);
    Task<bool> RunExistsAsync(Guid runId, CancellationToken cancellationToken);
    Task<AccessImportRunStatusData?> FindRunStatusAsync(Guid runId, CancellationToken cancellationToken);
    Task<AccessImportRunLogEntryListData> GetRunLogEntryListDataAsync(Guid runId, int limit, CancellationToken cancellationToken);
    Task<AccessImportCreatedRecordListData> GetCreatedRecordListDataAsync(Guid runId, int limit, CancellationToken cancellationToken);
    Task<AccessImportRun?> FindRunAsync(Guid runId, bool trackChanges, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetQueuedRunIdsAsync(CancellationToken cancellationToken);
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

public sealed record AccessImportRunListItemData(
    Guid Id,
    string Status,
    string OriginalFileName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary);

public sealed record AccessImportRunStatusData(
    Guid Id,
    string Status,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary);

public sealed record AccessImportRunLogEntryListData(
    bool RunExists,
    IReadOnlyList<AccessImportRunLogEntryListItemData> Entries);

public sealed record AccessImportRunLogEntryListItemData(
    Guid Id,
    Guid AccessImportRunId,
    DateTimeOffset CreatedAtUtc,
    string Level,
    string StepCode,
    string Message);

public sealed record AccessImportCreatedRecordListData(
    bool RunExists,
    IReadOnlyList<AccessImportCreatedRecord> Records);

public sealed record AccessImportAuditData(
    int CreatedRecordCount,
    int PendingRollbackRecordCount,
    int SourceRowFingerprintCount,
    IReadOnlyList<string> TargetEntityTypes,
    IReadOnlyList<string> SourceRowFingerprints);
