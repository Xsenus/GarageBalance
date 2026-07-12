using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierGroupRepository(GarageBalanceDbContext dbContext) : ISupplierGroupRepository
{
    public async Task<IReadOnlyList<SupplierGroup>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = BaseQuery(includeArchived);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await query.OrderBy(group => group.Name).ToListAsync(cancellationToken))
                .Where(group => group.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();
        }

        query = ApplySearch(query, normalizedSearch);
        return await query.OrderBy(group => group.Name).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<SupplierGroupPageData> GetPageAsync(
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = BaseQuery(includeArchived);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await query.OrderBy(group => group.Name).ToListAsync(cancellationToken))
                .Where(group => group.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new SupplierGroupPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(group => group.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new SupplierGroupPageData(items, totalCount);
    }

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken)
    {
        return dbContext.SupplierGroups.AnyAsync(
            group => !group.IsArchived && group.Name == name && (!ignoredId.HasValue || group.Id != ignoredId.Value),
            cancellationToken);
    }

    public Task<SupplierGroup?> FindActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierGroups.SingleOrDefaultAsync(group => group.Id == id && !group.IsArchived, cancellationToken);
    }

    public Task<SupplierGroup?> FindArchivedAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierGroups.SingleOrDefaultAsync(group => group.Id == id && group.IsArchived, cancellationToken);
    }

    public void Add(SupplierGroup group)
    {
        dbContext.SupplierGroups.Add(group);
    }

    private IQueryable<SupplierGroup> BaseQuery(bool includeArchived) =>
        dbContext.SupplierGroups.AsNoTracking().Where(group => includeArchived || !group.IsArchived);

    private static IQueryable<SupplierGroup> ApplySearch(IQueryable<SupplierGroup> query, string? normalizedSearch) =>
        normalizedSearch is null ? query : query.Where(group => group.Name.ToLower().Contains(normalizedSearch));

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
