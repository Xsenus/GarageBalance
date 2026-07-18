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

    Task<AccrualPageData> GetDueDateReviewPageAsync(int offset, int limit, CancellationToken cancellationToken);

    Task<decimal> GetTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<OverdueAccrualDebtData>> GetOverdueDebtDetailsAsync(Guid garageId, DateOnly asOfDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccrualBucketData>> GetMonthlyBucketsAsync(Guid garageId, DateOnly? monthFrom, DateOnly monthTo, CancellationToken cancellationToken);
    Task<Accrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<Accrual?> FindActiveForUpdateAsync(Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<IReadOnlyList<Accrual>> GetActiveMeteredForUpdateAsync(
        Guid garageId,
        DateOnly accountingMonth,
        string meterKind,
        CancellationToken cancellationToken);
    Task<int> CountActiveForGenerationAsync(Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetActiveGarageIdsAsync(Guid incomeTypeId, DateOnly accountingMonth, string source, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetActiveFeeCampaignGarageIdsAsync(Guid feeCampaignId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<int> CountActiveAnnualRegularForGenerationAsync(Guid incomeTypeId, int accountingYear, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetActiveAnnualRegularGarageIdsAsync(Guid incomeTypeId, int accountingYear, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid garageId, Guid incomeTypeId, DateOnly accountingMonth, int? accountingYear, string source, CancellationToken cancellationToken);
    Task<bool> ActiveIrregularDuplicateExistsAsync(Guid? ignoredId, Guid garageId, Guid irregularPaymentId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<bool> ActiveFeeCampaignDuplicateExistsAsync(Guid? ignoredId, Guid garageId, Guid feeCampaignId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<decimal> GetTotalThroughMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken);
    void Add(Accrual accrual);
}

public sealed record AccrualPageData(IReadOnlyList<Accrual> Items, int TotalCount);
public sealed record AccrualBucketData(DateOnly AccountingMonth, decimal Amount);
public sealed record OverdueAccrualDebtData(
    Guid AccrualId,
    Guid IncomeTypeId,
    string IncomeTypeName,
    DateOnly AccountingMonth,
    DateOnly DueDate,
    DateOnly OverdueFromDate,
    decimal Amount,
    decimal PaidAmount,
    decimal OutstandingAmount);
