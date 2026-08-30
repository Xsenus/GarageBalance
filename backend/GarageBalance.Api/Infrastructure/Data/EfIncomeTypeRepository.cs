using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfIncomeTypeRepository(GarageBalanceDbContext dbContext) : IIncomeTypeRepository
{
    public async Task<IReadOnlyList<IncomeType>> GetListAsync(
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

    public async Task<IncomeTypePageData> GetPageAsync(
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
            return new IncomeTypePageData(filteredItems.Skip(offset).Take(limit).ToList(), filteredItems.Count);
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
        return new IncomeTypePageData(items, totalCount);
    }

    private async Task<IncomeTypePageData> GetPostgresPageAsync(
        IQueryable<IncomeType> query,
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
            .Select(item => new IncomeTypePageRow
            {
                Category = PageCategory,
                Id = item.Id,
                Name = item.Name,
                Code = item.Code,
                DestinationFundId = item.DestinationFundId,
                DestinationFundName = item.DestinationFund == null ? null : item.DestinationFund.Name,
                IsSystem = item.IsSystem,
                IsArchived = item.IsArchived,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new IncomeTypePageRow
            {
                Category = TotalsCategory,
                Id = null,
                Name = null,
                Code = null,
                DestinationFundId = null,
                DestinationFundName = null,
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
            .Select(row => new IncomeType
            {
                Id = row.Id!.Value,
                Name = row.Name!,
                Code = row.Code,
                DestinationFundId = row.DestinationFundId,
                DestinationFund = row.DestinationFundId.HasValue
                    ? new Fund { Id = row.DestinationFundId.Value, Name = row.DestinationFundName! }
                    : null,
                IsSystem = row.IsSystem!.Value,
                IsArchived = row.IsArchived!.Value
            })
            .ToList();
        return new IncomeTypePageData(items, totalCount);
    }

    public Task<IncomeType?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes
            .Include(item => item.DestinationFund)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public async Task<IReadOnlyList<IncomeType>> GetActiveByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.IncomeTypes
            .Include(item => item.DestinationFund)
            .Where(item => ids.Contains(item.Id) && !item.IsArchived)
            .ToListAsync(cancellationToken);
    }

    public Task<IncomeType?> FindFirstActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes.FirstOrDefaultAsync(item => !item.IsArchived && item.Code == code, cancellationToken);

    public Task<IncomeType?> FindFirstActiveByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes.FirstOrDefaultAsync(item => !item.IsArchived && item.Name == name, cancellationToken);

    public Task<IncomeType?> FindFirstArchivedByCodeOrNameAsync(string code, string name, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes.FirstOrDefaultAsync(item => item.IsArchived && (item.Code == code || item.Name == name), cancellationToken);

    public Task<IncomeType?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes
            .Include(item => item.DestinationFund)
            .SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> ActiveCodeExistsAsync(Guid? ignoredId, string normalizedCode, CancellationToken cancellationToken) =>
        dbContext.IncomeTypes.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Code == normalizedCode && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> HasActiveServiceAssignmentsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.AsNoTracking()
            .AnyAsync(setting => setting.IncomeTypeId == id && !setting.IsArchived, cancellationToken);

    public void Add(IncomeType incomeType) => dbContext.IncomeTypes.Add(incomeType);

    private IQueryable<IncomeType> ApplyArchiveFilter(bool includeArchived) =>
        dbContext.IncomeTypes.AsNoTracking()
            .Include(item => item.DestinationFund)
            .Where(item => includeArchived || !item.IsArchived);

    private IQueryable<IncomeType> ApplySearch(IQueryable<IncomeType> query, string? normalizedSearch)
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

    private static bool MatchesSearch(IncomeType item, string normalizedSearch) =>
        item.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (item.Code?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class IncomeTypePageRow
    {
        public int Category { get; init; }
        public Guid? Id { get; init; }
        public string? Name { get; init; }
        public string? Code { get; init; }
        public Guid? DestinationFundId { get; init; }
        public string? DestinationFundName { get; init; }
        public bool? IsSystem { get; init; }
        public bool? IsArchived { get; init; }
        public int TotalCount { get; init; }
    }
}
