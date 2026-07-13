using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Funds;

public interface IFundRepository
{
    Task<IReadOnlyList<Fund>> GetFundsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FundOperation>> GetRecentOperationsAsync(int limit, bool includeCanceled, CancellationToken cancellationToken);
    Task<FundOperationPageData> GetOperationsPageAsync(int offset, int limit, bool includeCanceled, CancellationToken cancellationToken);
    Task<Fund?> FindFundForUpdateAsync(Guid fundId, CancellationToken cancellationToken);
    Task<FundOperation?> FindOperationForUpdateAsync(Guid operationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetNormalizedFundNamesAsync(CancellationToken cancellationToken);
    Task<FundTotalsData> GetTotalsAsync(CancellationToken cancellationToken);
    Task<decimal> GetActiveDepositTotalAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FundOperation>> GetOperationsOrderedAsync(Guid fundId, bool trackChanges, CancellationToken cancellationToken);
    void AddFund(Fund fund);
    void AddOperation(FundOperation operation);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record FundTotalsData(decimal IncomeTotal, decimal ExpenseTotal, decimal AllocatedFundTotal);

public sealed record FundOperationPageData(IReadOnlyList<FundOperation> Items, int TotalCount);
