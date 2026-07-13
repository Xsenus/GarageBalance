using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageRepository(GarageBalanceDbContext dbContext) : IGarageRepository
{
    public async Task<IReadOnlyList<Garage>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var garages = await query.OrderBy(garage => garage.Number).ToListAsync(cancellationToken);
            return garages.Where(garage => GarageMatchesSearch(garage, searchValue)).Take(limit).ToList();
        }

        return await ApplySearch(query, normalizedSearch)
            .OrderBy(garage => garage.Number)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<GaragePageData> GetPageAsync(
        string? normalizedSearch,
        bool includeArchived,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var garages = await ApplyPageSorting(query, sortBy, sortDescending).ThenBy(garage => garage.Id).ToListAsync(cancellationToken);
            var filtered = garages.Where(garage => GarageMatchesSearch(garage, searchValue)).ToList();
            return new GaragePageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await ApplyPageSorting(query, sortBy, sortDescending)
            .ThenBy(garage => garage.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new GaragePageData(items, totalCount);
    }

    public async Task<GarageBalanceTotalsData> GetBalanceTotalsAsync(
        IReadOnlyCollection<Guid> garageIds,
        CancellationToken cancellationToken)
    {
        var accrualTotals = await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && garageIds.Contains(accrual.GarageId))
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        var incomeTotals = await dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId.HasValue &&
                garageIds.Contains(operation.GarageId.Value))
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new { GarageId = group.Key, Amount = group.Sum(operation => operation.Amount) })
            .ToDictionaryAsync(item => item.GarageId, item => item.Amount, cancellationToken);
        return new GarageBalanceTotalsData(accrualTotals, incomeTotals);
    }

    public Task<Garage?> FindActiveWithOwnerAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Garages.Include(garage => garage.Owner)
            .SingleOrDefaultAsync(garage => garage.Id == id && !garage.IsArchived, cancellationToken);

    public Task<Garage?> FindArchivedWithOwnerAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Garages.Include(garage => garage.Owner)
            .SingleOrDefaultAsync(garage => garage.Id == id && garage.IsArchived, cancellationToken);

    public async Task<IReadOnlyList<Garage>> GetAllActiveWithOwnerAsync(CancellationToken cancellationToken) =>
        await dbContext.Garages
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Garage>> GetActiveByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        await dbContext.Garages
            .Where(garage => ids.Contains(garage.Id) && !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);

    public Task<decimal> GetStartingBalanceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Garages
            .Where(garage => garage.Id == id)
            .Select(garage => garage.StartingBalance)
            .SingleAsync(cancellationToken);

    public Task<bool> ActiveNumberExistsAsync(Guid? ignoredId, string number, CancellationToken cancellationToken) =>
        dbContext.Garages.AsNoTracking().AnyAsync(
            garage => !garage.IsArchived && garage.Number == number && (!ignoredId.HasValue || garage.Id != ignoredId.Value),
            cancellationToken);

    public void Add(Garage garage) => dbContext.Garages.Add(garage);

    private IQueryable<Garage> ApplyArchiveFilter(bool includeArchived) =>
        dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => includeArchived || !garage.IsArchived);

    private static IQueryable<Garage> ApplySearch(IQueryable<Garage> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(garage =>
            garage.Number.ToLower().Contains(normalizedSearch) ||
            (garage.Owner != null && (
                garage.Owner.LastName.ToLower().Contains(normalizedSearch) ||
                garage.Owner.FirstName.ToLower().Contains(normalizedSearch) ||
                (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedSearch)) ||
                (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedSearch))));
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static IOrderedQueryable<Garage> ApplyPageSorting(IQueryable<Garage> query, string sortBy, bool descending)
    {
        return (sortBy, descending) switch
        {
            ("peopleCount", true) => query.OrderByDescending(garage => garage.PeopleCount),
            ("peopleCount", false) => query.OrderBy(garage => garage.PeopleCount),
            ("floorCount", true) => query.OrderByDescending(garage => garage.FloorCount),
            ("floorCount", false) => query.OrderBy(garage => garage.FloorCount),
            ("owner", true) => query.OrderByDescending(garage => garage.Owner == null ? null : garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)),
            ("owner", false) => query.OrderBy(garage => garage.Owner == null ? null : garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)),
            ("phone", true) => query.OrderByDescending(garage => garage.Owner == null ? null : garage.Owner.Phone),
            ("phone", false) => query.OrderBy(garage => garage.Owner == null ? null : garage.Owner.Phone),
            (_, true) => query.OrderByDescending(garage => garage.Number),
            _ => query.OrderBy(garage => garage.Number)
        };
    }

    private static bool GarageMatchesSearch(Garage garage, string normalizedSearch) =>
        garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
}
