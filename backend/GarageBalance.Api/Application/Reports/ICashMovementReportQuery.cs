using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Reports;

public interface ICashMovementReportQuery
{
    Task<CashPaymentReportData> GetCashPaymentsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken);

    Task<BankDepositReportData> GetBankDepositsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken);
}

public sealed record CashPaymentReportData(
    IReadOnlyList<CashPaymentQueryRow> Operations,
    decimal Total,
    int RowCount);

public sealed record CashPaymentQueryRow(
    Guid Id,
    DateOnly OperationDate,
    decimal Amount,
    string? SupplierName,
    string? ExpenseTypeName,
    string? DocumentNumber,
    string? Comment);

public sealed record BankDepositReportData(
    IReadOnlyList<FundOperation> Operations,
    decimal Total,
    int RowCount);
