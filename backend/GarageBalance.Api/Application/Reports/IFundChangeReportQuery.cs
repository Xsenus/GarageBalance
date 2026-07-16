namespace GarageBalance.Api.Application.Reports;

public interface IFundChangeReportQuery
{
    Task<FundChangeReportData> GetFundChangesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        CancellationToken cancellationToken);
}

public sealed record FundChangeReportData(
    IReadOnlyList<FundChangeReportQueryRow> Rows,
    decimal DepositTotal,
    decimal WithdrawalTotal,
    int RowCount);

public sealed record FundChangeReportQueryRow(
    Guid Id,
    Guid FundId,
    string FundName,
    DateTimeOffset CreatedAtUtc,
    string OperationKind,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Reason);
