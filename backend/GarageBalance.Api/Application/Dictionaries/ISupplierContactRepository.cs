using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface ISupplierContactRepository
{
    Task<IReadOnlyList<SupplierContact>> GetListAsync(Guid? supplierId, string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<SupplierContactPageData> GetPageAsync(Guid? supplierId, string? normalizedSearch, bool includeArchived, int offset, int limit, string sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<SupplierContact?> FindActiveWithSupplierAsync(Guid id, CancellationToken cancellationToken);
    Task<SupplierContact?> FindArchivedWithSupplierGroupAsync(Guid id, CancellationToken cancellationToken);
    void Add(SupplierContact contact);
}

public sealed record SupplierContactPageData(IReadOnlyList<SupplierContact> Items, int TotalCount);
