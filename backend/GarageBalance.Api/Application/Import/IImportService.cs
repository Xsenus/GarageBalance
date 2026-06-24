namespace GarageBalance.Api.Application.Import;

public interface IImportService
{
    Task<IReadOnlyList<AccessImportRunDto>> GetAccessImportRunsAsync(CancellationToken cancellationToken);
    Task<ImportResult<IReadOnlyList<AccessImportRunLogEntryDto>>> GetAccessImportRunLogEntriesAsync(Guid runId, CancellationToken cancellationToken);
    Task<ImportResult<ImportReportFileDto>> ExportAccessImportRunReportAsync(Guid runId, CancellationToken cancellationToken);
    Task<ImportResult<AccessImportRunDto>> DryRunAccessImportAsync(AccessImportDryRunRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
