using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierRepository(GarageBalanceDbContext dbContext) : ISupplierRepository
{
    public async Task<IReadOnlyList<Supplier>> GetListAsync(
        Guid? groupId,
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyFilters(groupId, normalizedSearch, includeArchived)
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierPageData> GetPageAsync(
        Guid? groupId,
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(groupId, normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(supplier => supplier.Group.Name)
            .ThenBy(supplier => supplier.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new SupplierPageData(items, totalCount);
    }

    public Task<Supplier?> FindActiveWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.Include(supplier => supplier.Group)
            .SingleOrDefaultAsync(supplier => supplier.Id == id && !supplier.IsArchived, cancellationToken);
    }

    public Task<Supplier?> FindArchivedWithGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.Include(supplier => supplier.Group)
            .SingleOrDefaultAsync(supplier => supplier.Id == id && supplier.IsArchived, cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> GetActiveByGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        await dbContext.Suppliers
            .Where(supplier => !supplier.IsArchived && supplier.GroupId == groupId)
            .OrderBy(supplier => supplier.Name)
            .ToListAsync(cancellationToken);

    public Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking()
            .Where(supplier => supplier.Id == id)
            .Select(supplier => supplier.StartingBalance)
            .SingleAsync(cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, Guid groupId, string name, CancellationToken cancellationToken)
    {
        return dbContext.Suppliers.AsNoTracking().AnyAsync(
            supplier =>
                supplier.GroupId == groupId &&
                !supplier.IsArchived &&
                supplier.Name == name &&
                (!ignoredId.HasValue || supplier.Id != ignoredId.Value),
            cancellationToken);
    }

    public void Add(Supplier supplier)
    {
        dbContext.Suppliers.Add(supplier);
    }

    private IQueryable<Supplier> ApplyFilters(Guid? groupId, string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.Suppliers.AsNoTracking()
            .Include(supplier => supplier.Group)
            .Where(supplier => includeArchived || !supplier.IsArchived);
        if (groupId is not null)
        {
            query = query.Where(supplier => supplier.GroupId == groupId);
        }

        if (normalizedSearch is not null)
        {
            query = query.Where(supplier =>
                supplier.Name.ToLower().Contains(normalizedSearch) ||
                (supplier.Inn != null && supplier.Inn.ToLower().Contains(normalizedSearch)) ||
                (supplier.ContactPerson != null && supplier.ContactPerson.ToLower().Contains(normalizedSearch)));
        }

        return query;
    }
}
