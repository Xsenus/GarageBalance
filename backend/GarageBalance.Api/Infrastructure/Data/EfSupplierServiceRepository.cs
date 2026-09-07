using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierServiceRepository(GarageBalanceDbContext context) : ISupplierServiceRepository
{
    public async Task<SupplierServicePageData> GetPageAsync(string? search, int offset, int limit, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = context.SupplierServices.AsNoTracking().Where(service => includeArchived || !service.IsArchived);
        if (search is not null)
        {
            // SQLite is only a test provider; its lower() does not fold Cyrillic.
            // Keep this fallback isolated from the paginated PostgreSQL path.
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                var filtered = (await query.OrderBy(service => service.Name).ThenBy(service => service.Id).ToListAsync(cancellationToken))
                    .Where(service => service.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                return new SupplierServicePageData(filtered.Skip(offset).Take(limit).ToArray(), filtered.Length);
            }
            var normalizedSearch = search.ToLowerInvariant();
            query = query.Where(service => service.Name.ToLower().Contains(normalizedSearch));
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(service => service.Name).ThenBy(service => service.Id)
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new SupplierServicePageData(items, totalCount);
    }

    public Task<SupplierService?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        context.SupplierServices.SingleOrDefaultAsync(service => service.Id == id && !service.IsArchived, cancellationToken);

    public Task<bool> ActiveNameExistsAsync(Guid? excludedId, string name, CancellationToken cancellationToken) =>
        context.SupplierServices.AnyAsync(service => service.Id != excludedId && !service.IsArchived && service.Name == name, cancellationToken);

    public void Add(SupplierService service) => context.SupplierServices.Add(service);
}
