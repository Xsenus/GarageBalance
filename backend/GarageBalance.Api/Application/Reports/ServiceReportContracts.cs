namespace GarageBalance.Api.Application.Reports;

public sealed record ServiceReportColumn(Guid Id, string Name, Guid[] ServiceIds);
public sealed record ServiceReportServiceOption(Guid Id, string Name, Guid? IncomeTypeId, bool IsArchived);
public sealed record ServiceReportColumnsDto(Guid Version, IReadOnlyList<ServiceReportColumn> Columns, IReadOnlyList<ServiceReportServiceOption> Services);
public sealed record UpdateServiceReportColumnsRequest(Guid Version, ServiceReportColumn[] Columns);
public sealed record ServiceReportRequest(DateOnly? DateFrom = null, DateOnly? DateTo = null, Guid? GarageId = null, int Offset = 0, int Limit = 50, bool OverdueOnly = false);
public sealed record ServiceReportRow(DateOnly? Date, Guid GarageId, string GarageNumber, decimal[] Amounts)
{
    public decimal Total => Amounts.Sum();
}
public sealed record ServiceReportDay(DateOnly Date, decimal[] Amounts)
{
    public decimal Total => Amounts.Sum();
}
public sealed record ServiceReportDto(DateOnly? DateFrom, DateOnly DateTo, IReadOnlyList<ServiceReportColumn> Columns,
    IReadOnlyList<ServiceReportRow> Rows, IReadOnlyList<ServiceReportDay> Days, decimal[] Totals, int RowCount, int Offset, int Limit)
{
    public decimal Total => Totals.Sum();
}

public interface IServiceReportRepository
{
    Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken);
    Task SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken);
    Task<ServiceReportDto> GetPaymentsAsync(ServiceReportRequest request, CancellationToken cancellationToken);
    Task<ServiceReportDto> GetDebtAsync(ServiceReportRequest request, CancellationToken cancellationToken);
}

public interface IServiceReportService
{
    Task<ServiceReportColumnsDto> GetColumnsAsync(CancellationToken cancellationToken);
    Task<ReportResult<ServiceReportColumnsDto>> SaveColumnsAsync(UpdateServiceReportColumnsRequest request, Guid? actorId, CancellationToken cancellationToken);
    Task<ReportResult<ServiceReportDto>> GetAsync(ServiceReportRequest request, bool debt, CancellationToken cancellationToken);
    Task<ReportResult<ReportExportFileDto>> ExportAsync(ServiceReportRequest request, bool debt, bool pdf, Guid? actorId, CancellationToken cancellationToken);
}
