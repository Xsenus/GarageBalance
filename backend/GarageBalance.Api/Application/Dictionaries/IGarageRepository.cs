using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageRepository
{
    Task<IReadOnlyList<Garage>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<GaragePageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<GarageBalanceTotalsData> GetBalanceTotalsAsync(IReadOnlyCollection<Guid> garageIds, CancellationToken cancellationToken);
    Task<Garage?> FindActiveWithOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<Garage?> FindArchivedWithOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Garage>> GetAllActiveWithOwnerAsync(CancellationToken cancellationToken);
    Task<int> CountActiveAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Garage>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveNumberExistsAsync(Guid? ignoredId, string number, CancellationToken cancellationToken);
    void Add(Garage garage);
}

public sealed record GaragePageData(IReadOnlyList<Garage> Items, int TotalCount);

public sealed record GarageBalanceTotalsData(
    IReadOnlyDictionary<Guid, decimal> AccrualTotals,
    IReadOnlyDictionary<Guid, decimal> IncomeTotals,
    IReadOnlyDictionary<Guid, decimal> OverdueAccrualTotals,
    IReadOnlyDictionary<Guid, decimal> AllocatedIncomeTotals);
