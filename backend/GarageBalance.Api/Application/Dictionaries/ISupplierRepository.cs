using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> GetListAsync(Guid? groupId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<SupplierPageData> GetPageAsync(Guid? groupId, string? normalizedSearch, bool includeArchived, int offset, int limit, CancellationToken cancellationToken);
    Task<Supplier?> FindActiveWithGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<Supplier?> FindArchivedWithGroupAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Supplier>> GetActiveByGroupAsync(Guid groupId, CancellationToken cancellationToken);
    Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid groupId, string name, CancellationToken cancellationToken);
    void Add(Supplier supplier);
}

public sealed record SupplierPageData(IReadOnlyList<Supplier> Items, int TotalCount);
