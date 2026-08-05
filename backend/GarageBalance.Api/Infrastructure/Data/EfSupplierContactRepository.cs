using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierContactRepository(GarageBalanceDbContext dbContext) : ISupplierContactRepository
{
    public async Task<IReadOnlyList<SupplierContact>> GetListAsync(
        Guid? supplierId,
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        return await ApplyFilters(supplierId, normalizedSearch, includeArchived)
            .OrderBy(contact => contact.Supplier.Name)
            .ThenBy(contact => contact.FullName)
            .ThenBy(contact => contact.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<SupplierContactPageData> GetPageAsync(
        Guid? supplierId,
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(supplierId, normalizedSearch, includeArchived);
        var totalCount = await query.CountAsync(cancellationToken);
        var orderedQuery = ApplyOrdering(query, sortBy, sortDescending);
        var items = await orderedQuery.Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new SupplierContactPageData(items, totalCount);
    }

    public Task<SupplierContact?> FindActiveWithSupplierAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierContacts.Include(contact => contact.Supplier)
            .SingleOrDefaultAsync(contact => contact.Id == id && !contact.IsArchived, cancellationToken);
    }

    public Task<SupplierContact?> FindArchivedWithSupplierGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.SupplierContacts.Include(contact => contact.Supplier)
            .ThenInclude(supplier => supplier.Group)
            .SingleOrDefaultAsync(contact => contact.Id == id && contact.IsArchived, cancellationToken);
    }

    public void Add(SupplierContact contact)
    {
        dbContext.SupplierContacts.Add(contact);
    }

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private IQueryable<SupplierContact> ApplyFilters(Guid? supplierId, string? normalizedSearch, bool includeArchived)
    {
        var query = dbContext.SupplierContacts.AsNoTracking()
            .Include(contact => contact.Supplier)
            .Where(contact => includeArchived || !contact.IsArchived);
        if (supplierId is not null)
        {
            query = query.Where(contact => contact.SupplierId == supplierId);
        }

        if (normalizedSearch is null)
        {
            return query;
        }

        if (IsNpgsqlProvider())
        {
            var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
            return query.Where(contact =>
                EF.Functions.ILike(contact.FullName, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\") ||
                (contact.Position != null && EF.Functions.ILike(contact.Position, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")) ||
                (contact.Phone != null && EF.Functions.ILike(contact.Phone, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")) ||
                (contact.Email != null && EF.Functions.ILike(contact.Email, EF.Functions.Collate(pattern, PostgresLikeSearch.UnicodeCollation), @"\")));
        }

        return query.Where(contact =>
            contact.FullName.ToLower().Contains(normalizedSearch) ||
            (contact.Position != null && contact.Position.ToLower().Contains(normalizedSearch)) ||
            (contact.Phone != null && contact.Phone.ToLower().Contains(normalizedSearch)) ||
            (contact.Email != null && contact.Email.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<SupplierContact> ApplyOrdering(IQueryable<SupplierContact> query, string sortBy, bool sortDescending)
    {
        IOrderedQueryable<SupplierContact> ordered = (sortBy, sortDescending) switch
        {
            ("supplier", true) => query.OrderByDescending(contact => contact.Supplier.Name),
            ("supplier", false) => query.OrderBy(contact => contact.Supplier.Name),
            ("position", true) => query.OrderByDescending(contact => contact.Position),
            ("position", false) => query.OrderBy(contact => contact.Position),
            ("status", true) => query.OrderByDescending(contact => contact.Status),
            ("status", false) => query.OrderBy(contact => contact.Status),
            (_, true) => query.OrderByDescending(contact => contact.FullName),
            _ => query.OrderBy(contact => contact.FullName)
        };
        return ordered.ThenBy(contact => contact.Id);
    }
}
