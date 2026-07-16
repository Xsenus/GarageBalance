namespace GarageBalance.Api.Application.Finance;

public interface IFinanceTotalsQuery
{
    Task<FinanceTotalsData> GetAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        CancellationToken cancellationToken);
}

public sealed record FinanceTotalsData(
    decimal IncomeTotal,
    decimal ExpenseTotal,
    decimal AccrualTotal,
    int OperationCount,
    int IncomeCount,
    int ExpenseCount,
    int AccrualCount,
    int MeterReadingCount,
    int SupplierAccrualCount);
