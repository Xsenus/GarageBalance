using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Reports;

public interface IFundChangeReportQuery
{
    Task<FundChangeReportData> GetFundChangesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int? limit,
        CancellationToken cancellationToken);
}

public sealed record FundChangeReportData(
    IReadOnlyList<FundOperation> Operations,
    decimal DepositTotal,
    decimal WithdrawalTotal,
    int RowCount,
    IReadOnlyDictionary<Guid, string> UsersById);
