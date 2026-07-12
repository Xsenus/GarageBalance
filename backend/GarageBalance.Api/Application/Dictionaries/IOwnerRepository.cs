using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IOwnerRepository
{
    Task<IReadOnlyList<Owner>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<OwnerPageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<Owner?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<Owner?> FindArchivedWithGaragesAsync(Guid id, CancellationToken cancellationToken);
    void Add(Owner owner);
}

public sealed record OwnerPageData(IReadOnlyList<Owner> Items, int TotalCount);
