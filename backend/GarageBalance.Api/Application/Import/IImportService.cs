namespace GarageBalance.Api.Application.Import;

public interface IImportService
{
    Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(AccessImportRunListRequest request, CancellationToken cancellationToken);
    Task<ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLogEntriesAsync(Guid runId, AccessImportRunLogListRequest request, CancellationToken cancellationToken);
    Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
