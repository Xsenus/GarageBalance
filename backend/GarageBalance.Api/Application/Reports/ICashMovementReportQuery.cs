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
        CancellationToken cancellationToken);

    Task<BankDepositReportData> GetBankDepositsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int? limit,
        CancellationToken cancellationToken);
}

public sealed record CashPaymentReportData(
    IReadOnlyList<FinancialOperation> Operations,
    decimal Total,
    int RowCount);

public sealed record BankDepositReportData(
    IReadOnlyList<FundOperation> Operations,
    decimal Total,
    int RowCount);
