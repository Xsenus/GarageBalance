namespace GarageBalance.Api.Application.Reports;

public interface IExpenseReportQuery
{
    Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken);
}

public sealed record ExpenseReportQueryData(
    decimal AccrualTotal,
    decimal ExpenseTotal,
    int RowCount,
    IReadOnlyList<ExpenseReportRowDto> Rows);
