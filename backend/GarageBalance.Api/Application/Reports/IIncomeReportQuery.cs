namespace GarageBalance.Api.Application.Reports;

public interface IIncomeReportQuery
{
    Task<IncomeReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        string? search,
        int? limit,
        int offset,
        CancellationToken cancellationToken);
}

public sealed record IncomeReportQueryData(
    decimal AccrualTotal,
    decimal IncomeTotal,
    int RowCount,
    IReadOnlyList<IncomeReportRowDto> Rows);
