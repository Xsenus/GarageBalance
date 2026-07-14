using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IAccrualRepository
{
    Task<IReadOnlyList<Accrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken);

    Task<AccrualPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<decimal> GetTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccrualBucketData>> GetMonthlyBucketsAsync(Guid garageId, DateOnly? monthFrom, DateOnly monthTo, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccrualIncomeTypeBucketData>> GetIncomeTypeBucketsAsync(Guid garageId, DateOnly monthFrom, DateOnly monthTo, CancellationToken cancellationToken);
    Task<AccrualSummaryData> GetSummaryAsync(DateOnly? monthFrom, DateOnly? monthTo, string? normalizedSearch, CancellationToken cancellationToken);
    Task<Accrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<Accrual?> FindActiveForUpdateAsync(Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetActiveGarageIdsAsync(Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<decimal> GetTotalThroughMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken);
    void Add(Accrual accrual);
}

public sealed record AccrualPageData(IReadOnlyList<Accrual> Items, int TotalCount);
public sealed record AccrualBucketData(DateOnly AccountingMonth, decimal Amount);
public sealed record AccrualIncomeTypeBucketData(DateOnly AccountingMonth, Guid IncomeTypeId, string IncomeTypeName, string? IncomeTypeCode, decimal Amount);
public sealed record AccrualSummaryData(decimal TotalAmount, int Count);
