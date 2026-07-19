namespace GarageBalance.Api.Application.Reports;

public interface IReportService
{
    Task<ReportResult<ConsolidatedReportDto>> GetConsolidatedReportAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<GarageDetailReportDto>> GetGarageReportAsync(GarageReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportGarageReportXlsxAsync(GarageReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportGarageReportPdfAsync(GarageReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportXlsxAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportConsolidatedReportPdfAsync(ConsolidatedReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<IncomeReportDto>> GetIncomeReportAsync(IncomeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ExpenseReportDto>> GetExpenseReportAsync(ExpenseReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<FundChangeReportDto>> GetFundChangeReportAsync(FundChangeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportXlsxAsync(FundChangeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportFundChangeReportPdfAsync(FundChangeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<CashPaymentReportDto>> GetCashPaymentReportAsync(CashPaymentReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportXlsxAsync(CashPaymentReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportCashPaymentReportPdfAsync(CashPaymentReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<BankDepositReportDto>> GetBankDepositReportAsync(BankDepositReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportXlsxAsync(BankDepositReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportBankDepositReportPdfAsync(BankDepositReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<FeeReportDto>> GetFeeReportAsync(FeeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportFeeReportXlsxAsync(FeeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportFeeReportPdfAsync(FeeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportIncomeReportXlsxAsync(IncomeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportIncomeReportPdfAsync(IncomeReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportExpenseReportXlsxAsync(ExpenseReportRequest request, CancellationToken cancellationToken);

    Task<ReportResult<ReportExportFileDto>> ExportExpenseReportPdfAsync(ExpenseReportRequest request, CancellationToken cancellationToken);
}
