using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public interface IFinancialOperationRepository
{
    Task<IReadOnlyList<FinancialOperation>> GetListAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int limit,
        CancellationToken cancellationToken);

    Task<FinancialOperationPageData> GetPageAsync(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? operationKind,
        string? normalizedSearch,
        Guid? garageId,
        Guid? supplierId,
        Guid? staffMemberId,
        int offset,
        int limit,
        CancellationToken cancellationToken);

    Task<FinancialOperation?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDocumentDuplicateExistsAsync(Guid? ignoredId, string operationKind, DateOnly operationDate, string documentNumber, CancellationToken cancellationToken);
    Task<decimal> GetIncomeTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<decimal> GetOpeningDebtPaymentTotalAsync(Guid garageId, DateOnly accountingMonth, string incomeTypeCode, string incomeTypeName, CancellationToken cancellationToken);
    Task<decimal> GetStaffExpenseTotalAsync(Guid staffMemberId, DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<decimal> GetPreviousGarageIncomeTotalAsync(Guid ignoredId, Guid garageId, DateOnly operationDate, CancellationToken cancellationToken);
    Task<decimal> GetPreviousSupplierExpenseTotalAsync(Guid ignoredId, Guid supplierId, DateOnly operationDate, CancellationToken cancellationToken);
    void Add(FinancialOperation operation);
}

public sealed record FinancialOperationPageData(IReadOnlyList<FinancialOperation> Items, int TotalCount);
