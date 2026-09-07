using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Dictionaries;

public sealed record SupplierServiceDto(Guid Id, string Name, bool IsArchived, Guid Version);
public sealed record UpsertSupplierServiceRequest([Required, StringLength(200)] string Name, Guid? Version = null);

public interface ISupplierServiceCatalog
{
    Task<PagedResult<SupplierServiceDto>> GetPageAsync(string? search, int offset, int limit, bool includeArchived, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierServiceDto>> CreateAsync(UpsertSupplierServiceRequest request, Guid? actorUserId, CancellationToken cancellationToken);
    Task<DictionaryResult<SupplierServiceDto>> UpdateAsync(Guid id, UpsertSupplierServiceRequest request, Guid? actorUserId, CancellationToken cancellationToken);
}
