using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IGarageRepository
{
    Task<IReadOnlyList<GarageListItemData>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<GaragePageData> GetPageAsync(string? normalizedSearch, bool includeArchived, bool debtorsOnly, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<GarageBalanceTotalsData> GetBalanceTotalsAsync(IReadOnlyCollection<Guid> garageIds, CancellationToken cancellationToken);
    Task<Garage?> FindActiveWithOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<Garage?> FindArchivedWithOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Garage>> GetAllActiveWithOwnerAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken);
    Task<int> CountActiveAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Garage>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveNumberExistsAsync(Guid? ignoredId, string number, CancellationToken cancellationToken);
    void Add(Garage garage);
}

public sealed record GaragePageData(IReadOnlyList<GarageListItemData> Items, int TotalCount);

public sealed record GarageListItemData(
    Guid Id,
    string Number,
    int PeopleCount,
    int FloorCount,
    Guid? OwnerId,
    string? OwnerName,
    string? OwnerPhone,
    decimal StartingBalance,
    decimal? InitialWaterMeterValue,
    decimal? InitialElectricityMeterValue,
    string? Comment,
    bool IsArchived);

public sealed record GarageBalanceTotalsData(
    IReadOnlyDictionary<Guid, decimal> AccrualTotals,
    IReadOnlyDictionary<Guid, decimal> IncomeTotals,
    IReadOnlyDictionary<Guid, decimal> OverdueAccrualTotals,
    IReadOnlyDictionary<Guid, decimal> AllocatedIncomeTotals);
