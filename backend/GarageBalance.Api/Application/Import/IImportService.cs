namespace GarageBalance.Api.Application.Import;

public interface IImportService
{
    Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(AccessImportRunListRequest request, CancellationToken cancellationToken);
    Task<ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLogEntriesAsync(Guid runId, AccessImportRunLogListRequest request, CancellationToken cancellationToken);
    Task<ImportResult<IReadOnlyList<AccessImportCreatedRecordDto>>> GetAccessImportCreatedRecordsAsync(Guid runId, AccessImportCreatedRecordListRequest request, CancellationToken cancellationToken);
    Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, Guid? actorUserId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> RequestAccessImportApplyAsync(Guid runId, AccessImportApplyRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> CancelAccessImportApplyRequestAsync(Guid runId, AccessImportApplyCancelRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> RequestAccessImportRollbackAsync(Guid runId, AccessImportRollbackRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
