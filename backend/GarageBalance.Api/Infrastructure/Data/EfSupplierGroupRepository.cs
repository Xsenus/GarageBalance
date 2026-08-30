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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(group => group.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new SupplierGroupPageData(items, totalCount);
    }

    private async Task<SupplierGroupPageData> GetPostgresPageAsync(
        IQueryable<SupplierGroup> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderBy(group => group.Name)
            .ThenBy(group => group.Id)
            .Skip(offset)
            .Take(limit)
            .Select(group => new SupplierGroupPageRow
            {
                Category = PageCategory,
                Id = group.Id,
                Name = group.Name,
                IsSystem = group.IsSystem,
                IsArchived = group.IsArchived,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new SupplierGroupPageRow
            {
                Category = TotalsCategory,
                Id = null,
                Name = null,
                IsSystem = null,
                IsArchived = null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new SupplierGroup
            {
                Id = row.Id!.Value,
                Name = row.Name!,
                IsSystem = row.IsSystem!.Value,
                IsArchived = row.IsArchived!.Value
            })
            .ToList();
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

    public Task<bool> HasActiveSuppliersAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking().AnyAsync(supplier => supplier.GroupId == id && !supplier.IsArchived, cancellationToken);

    public void Add(SupplierGroup group)
    {
        dbContext.SupplierGroups.Add(group);
    }

    private IQueryable<SupplierGroup> BaseQuery(bool includeArchived) =>
        dbContext.SupplierGroups.AsNoTracking().Where(group => includeArchived || !group.IsArchived);

    private IQueryable<SupplierGroup> ApplySearch(IQueryable<SupplierGroup> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        if (dbContext.Database.IsNpgsql())
        {
            var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
            return query.Where(group => EF.Functions.ILike(group.Name, pattern, @"\"));
        }

        return query.Where(group => group.Name.ToLower().Contains(normalizedSearch));
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class SupplierGroupPageRow
    {
        public int Category { get; init; }
        public Guid? Id { get; init; }
        public string? Name { get; init; }
        public bool? IsSystem { get; init; }
        public bool? IsArchived { get; init; }
        public int TotalCount { get; init; }
    }
}
