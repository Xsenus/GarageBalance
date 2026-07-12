using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface ITariffRepository
{
    Task<IReadOnlyList<Tariff>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<TariffPageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<Tariff?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<Tariff?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, DateOnly effectiveFrom, CancellationToken cancellationToken);
    Task<DateOnly?> GetEarliestRegularAccrualMonthAsync(Guid id, CancellationToken cancellationToken);
    void Add(Tariff tariff);
}

public sealed record TariffPageData(IReadOnlyList<Tariff> Items, int TotalCount);
