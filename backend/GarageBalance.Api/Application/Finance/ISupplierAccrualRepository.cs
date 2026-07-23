using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface ISupplierAccrualRepository
{
    Task<IReadOnlyList<SupplierAccrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        Guid? supplierId,
        int limit,
        CancellationToken cancellationToken);

    Task<SupplierAccrualPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        Guid? supplierId,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<int> CountActiveAsync(DateOnly? monthFrom, DateOnly? monthTo, string? normalizedSearch, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetActiveSupplierIdsAsync(Guid expenseTypeId, DateOnly accountingMonth, string source, string? documentNumber, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid supplierId, Guid expenseTypeId, DateOnly accountingMonth, string source, string? documentNumber, CancellationToken cancellationToken);
    Task<SupplierAccrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierAccrual?> FindBySourceFinancialOperationForUpdateAsync(Guid operationId, CancellationToken cancellationToken);
    Task<decimal> GetTotalThroughMonthAsync(Guid supplierId, Guid expenseTypeId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupplierAccrualBucketData>> GetMonthlyBucketsThroughMonthAsync(Guid supplierId, Guid expenseTypeId, DateOnly accountingMonth, CancellationToken cancellationToken);
    void Add(SupplierAccrual accrual);
}

public sealed record SupplierAccrualPageData(IReadOnlyList<SupplierAccrual> Items, int TotalCount);
public sealed record SupplierAccrualBucketData(DateOnly AccountingMonth, decimal Amount);
