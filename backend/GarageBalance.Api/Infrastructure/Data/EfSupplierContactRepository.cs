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
        return await IncludeSupplier(ApplyFilters(supplierId, normalizedSearch, includeArchived))
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
        if (IsNpgsqlProvider())
        {
            return await GetPostgresPageAsync(query, offset, limit, sortBy, sortDescending, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orderedQuery = ApplyOrdering(IncludeSupplier(query), sortBy, sortDescending);
        var items = await orderedQuery.Skip(offset).Take(limit).ToListAsync(cancellationToken);
        return new SupplierContactPageData(items, totalCount);
    }

    private async Task<SupplierContactPageData> GetPostgresPageAsync(
        IQueryable<SupplierContact> query,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var projectedRows = query.Select(contact => new SupplierContactListRow
        {
            Category = PageCategory,
            ContactId = contact.Id,
            SupplierId = contact.SupplierId,
            SupplierName = contact.Supplier.Name,
            FullName = contact.FullName,
            Position = contact.Position,
            Phone = contact.Phone,
            Email = contact.Email,
            Status = contact.Status,
            Comment = contact.Comment,
            IsArchived = contact.IsArchived,
            TotalCount = 0
        });
        var pageRows = ApplyPostgresOrdering(projectedRows, sortBy, sortDescending)
            .Skip(offset)
            .Take(limit);
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new SupplierContactListRow
            {
                Category = TotalsCategory,
                ContactId = null,
                SupplierId = null,
                SupplierName = null,
                FullName = null,
                Position = null,
                Phone = null,
                Email = null,
                Status = null,
                Comment = null,
                IsArchived = null,
                TotalCount = query.Count()
            });
        var rows = await ApplyPostgresOrderingByCategory(
                pageRows.Concat(totalsRow),
                sortBy,
                sortDescending)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new SupplierContact
            {
                Id = row.ContactId!.Value,
                SupplierId = row.SupplierId!.Value,
                Supplier = new Supplier { Id = row.SupplierId.Value, Name = row.SupplierName! },
                FullName = row.FullName!,
                Position = row.Position,
                Phone = row.Phone,
                Email = row.Email,
                Status = row.Status!,
                Comment = row.Comment,
                IsArchived = row.IsArchived!.Value
            })
            .ToList();
        return new SupplierContactPageData(items, totalCount);
    }

    private static IOrderedQueryable<SupplierContactListRow> ApplyPostgresOrdering(
        IQueryable<SupplierContactListRow> query,
        string sortBy,
        bool descending)
    {
        IOrderedQueryable<SupplierContactListRow> ordered = (sortBy, descending) switch
        {
            ("supplier", true) => query.OrderByDescending(row => row.SupplierName),
            ("supplier", false) => query.OrderBy(row => row.SupplierName),
            ("position", true) => query.OrderByDescending(row => row.Position),
            ("position", false) => query.OrderBy(row => row.Position),
            ("status", true) => query.OrderByDescending(row => row.Status),
            ("status", false) => query.OrderBy(row => row.Status),
            (_, true) => query.OrderByDescending(row => row.FullName),
            _ => query.OrderBy(row => row.FullName)
        };
        return ordered.ThenBy(row => row.ContactId);
    }

    private static IOrderedQueryable<SupplierContactListRow> ApplyPostgresOrderingByCategory(
        IQueryable<SupplierContactListRow> query,
        string sortBy,
        bool descending)
    {
        IOrderedQueryable<SupplierContactListRow> ordered = (sortBy, descending) switch
        {
            ("supplier", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.SupplierName),
            ("supplier", false) => query.OrderBy(row => row.Category).ThenBy(row => row.SupplierName),
            ("position", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.Position),
            ("position", false) => query.OrderBy(row => row.Category).ThenBy(row => row.Position),
            ("status", true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.Status),
            ("status", false) => query.OrderBy(row => row.Category).ThenBy(row => row.Status),
            (_, true) => query.OrderBy(row => row.Category).ThenByDescending(row => row.FullName),
            _ => query.OrderBy(row => row.Category).ThenBy(row => row.FullName)
        };
        return ordered.ThenBy(row => row.ContactId);
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

    private static IQueryable<SupplierContact> IncludeSupplier(IQueryable<SupplierContact> query) =>
        query.Include(contact => contact.Supplier);

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

    private sealed class SupplierContactListRow
    {
        public int Category { get; init; }
        public Guid? ContactId { get; init; }
        public Guid? SupplierId { get; init; }
        public string? SupplierName { get; init; }
        public string? FullName { get; init; }
        public string? Position { get; init; }
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? Status { get; init; }
        public string? Comment { get; init; }
        public bool? IsArchived { get; init; }
        public int TotalCount { get; init; }
    }
}
