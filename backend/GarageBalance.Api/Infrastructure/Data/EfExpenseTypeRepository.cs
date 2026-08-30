using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseTypeRepository(GarageBalanceDbContext dbContext) : IExpenseTypeRepository
{
    public async Task<IReadOnlyList<ExpenseType>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await query.OrderBy(item => item.Name).ToListAsync(cancellationToken))
                .Where(item => MatchesSearch(item, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await ApplySearch(query, normalizedSearch)
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseTypePageData> GetPageAsync(
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filteredItems = (await query.OrderBy(item => item.Name).ToListAsync(cancellationToken))
                .Where(item => MatchesSearch(item, normalizedSearch))
                .ToList();
            return new ExpenseTypePageData(filteredItems.Skip(offset).Take(limit).ToList(), filteredItems.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(item => item.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new ExpenseTypePageData(items, totalCount);
    }

    private async Task<ExpenseTypePageData> GetPostgresPageAsync(
        IQueryable<ExpenseType> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip(offset)
            .Take(limit)
            .Select(item => new ExpenseTypePageRow
            {
                Category = PageCategory,
                Id = item.Id,
                Name = item.Name,
                Code = item.Code,
                IsSystem = item.IsSystem,
                IsArchived = item.IsArchived,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new ExpenseTypePageRow
            {
                Category = TotalsCategory,
                Id = null,
                Name = null,
                Code = null,
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
            .Select(row => new ExpenseType
            {
                Id = row.Id!.Value,
                Name = row.Name!,
                Code = row.Code,
                IsSystem = row.IsSystem!.Value,
                IsArchived = row.IsArchived!.Value
            })
            .ToList();
        return new ExpenseTypePageData(items, totalCount);
    }

    public Task<ExpenseType?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<ExpenseType?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Code == code && !item.IsArchived, cancellationToken);

    public Task<ExpenseType?> FindActiveByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Name == name && !item.IsArchived, cancellationToken);

    public Task<ExpenseType?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> ActiveCodeExistsAsync(Guid? ignoredId, string normalizedCode, CancellationToken cancellationToken) =>
        dbContext.ExpenseTypes.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Code == normalizedCode && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> HasActiveServiceAssignmentsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Suppliers.AsNoTracking()
            .AnyAsync(supplier => supplier.ExpenseTypeId == id && !supplier.IsArchived, cancellationToken);

    public void Add(ExpenseType expenseType) => dbContext.ExpenseTypes.Add(expenseType);

    private IQueryable<ExpenseType> ApplyArchiveFilter(bool includeArchived) =>
        dbContext.ExpenseTypes.AsNoTracking().Where(item => includeArchived || !item.IsArchived);

    private IQueryable<ExpenseType> ApplySearch(IQueryable<ExpenseType> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        if (dbContext.Database.IsNpgsql())
        {
            var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
            return query.Where(item =>
                EF.Functions.ILike(item.Name, pattern, @"\") ||
                (item.Code != null && EF.Functions.ILike(item.Code, pattern, @"\")));
        }

        return query.Where(item =>
            item.Name.ToLower().Contains(normalizedSearch) ||
            (item.Code != null && item.Code.ToLower().Contains(normalizedSearch)));
    }

    private static bool MatchesSearch(ExpenseType item, string normalizedSearch) =>
        item.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (item.Code?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class ExpenseTypePageRow
    {
        public int Category { get; init; }
        public Guid? Id { get; init; }
        public string? Name { get; init; }
        public string? Code { get; init; }
        public bool? IsSystem { get; init; }
        public bool? IsArchived { get; init; }
        public int TotalCount { get; init; }
    }
}
