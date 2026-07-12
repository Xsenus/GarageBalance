using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface ISupplierGroupRepository
{
    Task<IReadOnlyList<SupplierGroup>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<SupplierGroupPageData> GetPageAsync(string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<SupplierGroup?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierGroup?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    void Add(SupplierGroup group);
}

public sealed record SupplierGroupPageData(IReadOnlyList<SupplierGroup> Items, int TotalCount);
