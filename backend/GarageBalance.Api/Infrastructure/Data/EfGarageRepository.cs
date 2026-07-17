using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageRepository(GarageBalanceDbContext dbContext, TimeProvider? timeProvider = null) : IGarageRepository
{
    private const int AccrualBalanceCategory = 1;
    private const int IncomeBalanceCategory = 2;
    private const int OverdueAccrualBalanceCategory = 3;
    private const int AllocatedIncomeBalanceCategory = 4;
    private DateOnly Today => DateOnly.FromDateTime((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);

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
        bool debtorsOnly,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (IsSqliteProvider() && (normalizedSearch is not null || sortBy == "overdueDebt" || debtorsOnly))
        {
            var garages = await query.ToListAsync(cancellationToken);
            var filtered = normalizedSearch is { } searchValue
                ? garages.Where(garage => GarageMatchesSearch(garage, searchValue)).ToList()
                : garages;
            var totals = sortBy == "overdueDebt" || debtorsOnly
                ? await GetBalanceTotalsAsync(filtered.Select(garage => garage.Id).ToArray(), cancellationToken)
                : EmptyBalanceTotals();
            if (debtorsOnly)
            {
                filtered = filtered
                    .Where(garage => !garage.IsArchived && CalculateOverdueDebt(garage, totals) > 0m)
                    .ToList();
            }
            var pageItems = ApplySqlitePageSorting(filtered, totals, sortBy, sortDescending)
                .Skip(offset)
                .Take(limit)
                .ToList();
            return new GaragePageData(pageItems, filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        if (debtorsOnly)
        {
            query = ApplyDebtorsFilter(query);
        }
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
        if (garageIds.Count == 0)
        {
            return EmptyBalanceTotals();
        }

        var accrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && garageIds.Contains(accrual.GarageId))
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                Category = AccrualBalanceCategory,
                GarageId = group.Key,
                Amount = group.Sum(accrual => accrual.Amount)
            });
        var incomeQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId.HasValue &&
                garageIds.Contains(operation.GarageId.Value))
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new
            {
                Category = IncomeBalanceCategory,
                GarageId = group.Key,
                Amount = group.Sum(operation => operation.Amount)
            });
        var today = Today;
        var overdueAccrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.OverdueFromDate <= today &&
                garageIds.Contains(accrual.GarageId))
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                Category = OverdueAccrualBalanceCategory,
                GarageId = group.Key,
                Amount = group.Sum(accrual => accrual.Amount) -
                    dbContext.AccrualPaymentAllocations
                        .Where(allocation =>
                            allocation.IsActive &&
                            !allocation.Accrual.IsCanceled &&
                            allocation.Accrual.OverdueFromDate <= today &&
                            allocation.Accrual.GarageId == group.Key &&
                            !allocation.FinancialOperation.IsCanceled)
                        .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            });
        var allocatedIncomeQuery = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                !allocation.Accrual.IsCanceled &&
                !allocation.FinancialOperation.IsCanceled &&
                garageIds.Contains(allocation.Accrual.GarageId))
            .GroupBy(allocation => allocation.Accrual.GarageId)
            .Select(group => new
            {
                Category = AllocatedIncomeBalanceCategory,
                GarageId = group.Key,
                Amount = group.Sum(allocation => allocation.Amount)
            });
        var rows = await accrualQuery
            .Concat(incomeQuery)
            .Concat(overdueAccrualQuery)
            .Concat(allocatedIncomeQuery)
            .ToListAsync(cancellationToken);
        var accrualTotals = rows
            .Where(row => row.Category == AccrualBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var incomeTotals = rows
            .Where(row => row.Category == IncomeBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var overdueAccrualTotals = rows
            .Where(row => row.Category == OverdueAccrualBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var allocatedIncomeTotals = rows
            .Where(row => row.Category == AllocatedIncomeBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.Amount);
        return new GarageBalanceTotalsData(accrualTotals, incomeTotals, overdueAccrualTotals, allocatedIncomeTotals);
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

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        dbContext.Garages.AsNoTracking().CountAsync(garage => !garage.IsArchived, cancellationToken);

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

    private IOrderedQueryable<Garage> ApplyPageSorting(IQueryable<Garage> query, string sortBy, bool descending)
    {
        var today = Today;
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
            ("overdueDebt", true) => query.OrderByDescending(garage => Math.Max(
                garage.StartingBalance +
                (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
                (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
                0m)),
            ("overdueDebt", false) => query.OrderBy(garage => Math.Max(
                garage.StartingBalance +
                (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
                (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
                0m)),
            (_, true) => query.OrderByDescending(garage => garage.Number),
            _ => query.OrderBy(garage => garage.Number)
        };
    }

    private IQueryable<Garage> ApplyDebtorsFilter(IQueryable<Garage> query)
    {
        var today = Today;
        return query.Where(garage =>
            !garage.IsArchived &&
            garage.StartingBalance +
            (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
            (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
            (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
            (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) > 0m);
    }

    private static IOrderedEnumerable<Garage> ApplySqlitePageSorting(
        IEnumerable<Garage> garages,
        GarageBalanceTotalsData totals,
        string sortBy,
        bool descending)
    {
        Func<Garage, decimal> overdueDebt = garage => CalculateOverdueDebt(garage, totals);

        var ordered = (sortBy, descending) switch
        {
            ("peopleCount", true) => garages.OrderByDescending(garage => garage.PeopleCount),
            ("peopleCount", false) => garages.OrderBy(garage => garage.PeopleCount),
            ("floorCount", true) => garages.OrderByDescending(garage => garage.FloorCount),
            ("floorCount", false) => garages.OrderBy(garage => garage.FloorCount),
            ("owner", true) => garages.OrderByDescending(garage => garage.Owner?.FullName),
            ("owner", false) => garages.OrderBy(garage => garage.Owner?.FullName),
            ("phone", true) => garages.OrderByDescending(garage => garage.Owner?.Phone),
            ("phone", false) => garages.OrderBy(garage => garage.Owner?.Phone),
            ("overdueDebt", true) => garages.OrderByDescending(overdueDebt),
            ("overdueDebt", false) => garages.OrderBy(overdueDebt),
            (_, true) => garages.OrderByDescending(garage => garage.Number),
            _ => garages.OrderBy(garage => garage.Number)
        };
        return ordered.ThenBy(garage => garage.Id);
    }

    private static decimal CalculateOverdueDebt(Garage garage, GarageBalanceTotalsData totals) =>
        Math.Max(
            garage.StartingBalance + totals.OverdueAccrualTotals.GetValueOrDefault(garage.Id) -
            (totals.IncomeTotals.GetValueOrDefault(garage.Id) - totals.AllocatedIncomeTotals.GetValueOrDefault(garage.Id)),
            0m);

    private static GarageBalanceTotalsData EmptyBalanceTotals() =>
        new(
            new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>());

    private static bool GarageMatchesSearch(Garage garage, string normalizedSearch) =>
        garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
}
