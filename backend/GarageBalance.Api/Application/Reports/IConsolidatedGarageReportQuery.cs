namespace GarageBalance.Api.Application.Reports;

public interface IConsolidatedGarageReportQuery
{
    Task<ConsolidatedGarageRowsData> GetGarageRowsAsync(
        string? search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken);
}

public sealed record ConsolidatedGarageRowsData(
    int RowCount,
    IReadOnlyList<ConsolidatedGarageRowData> Rows);

public sealed record ConsolidatedGarageRowData(
    Guid GarageId,
    string GarageNumber,
    string? OwnerLastName,
    string? OwnerFirstName,
    string? OwnerMiddleName,
    decimal IncomeTotal,
    decimal AccrualTotal,
    int MeterReadingCount);

public readonly record struct AmountByGarage(Guid GarageId, decimal Amount);

public readonly record struct CountByGarage(Guid GarageId, int Count);
