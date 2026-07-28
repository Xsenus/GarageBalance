namespace GarageBalance.Api.Application.Reports;

public interface IGarageReportQuickListService
{
    Task<IReadOnlyList<GarageReportQuickListDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<ReportResult<GarageReportQuickListDto>> CreateAsync(
        UpsertGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<ReportResult<GarageReportQuickListDto>> UpdateAsync(
        Guid id,
        UpsertGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<ReportResult<bool>> DeleteAsync(
        Guid id,
        DeleteGarageReportQuickListRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
