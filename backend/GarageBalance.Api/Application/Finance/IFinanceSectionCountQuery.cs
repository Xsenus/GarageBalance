namespace GarageBalance.Api.Application.Finance;

public interface IFinanceSectionCountQuery
{
    Task<FinanceSectionCountData> GetAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        CancellationToken cancellationToken);
}

public sealed record FinanceSectionCountData(int MeterReadingCount, int SupplierAccrualCount);
