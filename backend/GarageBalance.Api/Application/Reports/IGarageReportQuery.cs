namespace GarageBalance.Api.Application.Reports;

public interface IGarageReportQuery
{
    Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        IReadOnlySet<Guid> garageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        bool groupAccruals,
        int offset,
        int limit,
        ReportSort sort,
        CancellationToken cancellationToken);
}

public sealed record GarageReportQueryData(
    decimal AccrualTotal,
    decimal IncomeTotal,
    int RowCount,
    IReadOnlyList<GarageReportQueryRow> Rows);

public sealed record GarageReportQueryRow(
    DateOnly AccountingMonth,
    Guid GarageId,
    string GarageNumber,
    string? OwnerLastName,
    string? OwnerFirstName,
    string? OwnerMiddleName,
    Guid? IncomeTypeId,
    string IncomeTypeName,
    decimal AccrualAmount,
    decimal IncomeAmount);
