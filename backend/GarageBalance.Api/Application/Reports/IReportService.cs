namespace GarageBalance.Api.Application.Reports;

public interface IReportService
{
    Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<IncomeReportDto>> GetIncomeReportAsync(IncomeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ExpenseReportDto>> GetExpenseReportAsync(ExpenseReportRequest request, CancellationToken cancellationToken);
}
