using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed record SupplierServicePageData(IReadOnlyList<SupplierService> Items, int TotalCount);

public interface ISupplierServiceRepository
{
    Task<SupplierServicePageData> GetPageAsync(string? search, int offset, int limit, bool includeArchived, CancellationToken cancellationToken);
    Task<SupplierService?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveNameExistsAsync(Guid? excludedId, string name, CancellationToken cancellationToken);
    void Add(SupplierService service);
}
