using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageRepository(GarageBalanceDbContext dbContext, IBusinessDateProvider? businessDateProvider = null) : IGarageRepository
{
    private const int AccrualBalanceCategory = 1;
    private const int IncomeBalanceCategory = 2;
    private const int AllocationBalanceCategory = 3;
    private DateOnly Today => businessDateProvider?.Today ?? DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<GarageListItemData>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (normalizedSearch is { } searchValue && IsSqliteProvider())
        {
            var garages = await ProjectListItems(query).ToListAsync(cancellationToken);
            return ApplySearchRanking(garages.Where(garage => GarageMatchesSearch(garage, searchValue)), searchValue)
                .Take(limit)
                .ToList();
        }

        var filteredQuery = ApplySearch(query, normalizedSearch);
        var orderedQuery = normalizedSearch is { } search
            ? ApplySearchRanking(filteredQuery, search)
            : filteredQuery.OrderBy(garage => garage.Number);
        return await ProjectListItems(orderedQuery)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<GaragePageData> GetPageAsync(
        string? normalizedSearch,
        GarageColumnFilters filters,
        bool includeArchived,
        bool debtorsOnly,
        int offset,
        int limit,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        var query = ApplyArchiveFilter(includeArchived);
        if (IsSqliteProvider() && (normalizedSearch is not null || HasColumnFilters(filters) || sortBy == "overdueDebt" || debtorsOnly))
        {
            var garages = await ProjectListItems(query).ToListAsync(cancellationToken);
            var filtered = normalizedSearch is { } searchValue
                ? garages.Where(garage => GarageMatchesSearch(garage, searchValue)).ToList()
                : garages;
            filtered = filtered.Where(garage => GarageMatchesColumnFilters(garage, filters)).ToList();
            var totals = sortBy == "overdueDebt" || debtorsOnly
                ? await GetBalanceTotalsAsync(filtered.Select(garage => garage.Id).ToArray(), cancellationToken)
                : EmptyBalanceTotals();
            if (debtorsOnly)
            {
                filtered = filtered
                    .Where(garage => !garage.IsArchived && CalculateOverdueDebt(garage, totals) > 0m)
                    .ToList();
            }
            var pageItems = normalizedSearch is { } search && sortBy == "number" && !sortDescending
                ? ApplySearchRanking(filtered, search).Skip(offset).Take(limit).ToList()
                : ApplySqlitePageSorting(filtered, totals, sortBy, sortDescending).Skip(offset).Take(limit).ToList();
            return new GaragePageData(pageItems, filtered.Count);
        }

        query = ApplyColumnFilters(ApplySearch(query, normalizedSearch), filters);
        if (debtorsOnly)
        {
            query = ApplyDebtorsFilter(query);
        }
        var totalCount = await query.CountAsync(cancellationToken);
        var orderedPageQuery = normalizedSearch is { } rankingSearch && sortBy == "number" && !sortDescending
            ? ApplySearchRanking(query, rankingSearch)
            : ApplyPageSorting(query, sortBy, sortDescending);
        var items = await ProjectListItems(orderedPageQuery
                .ThenBy(garage => garage.Id)
                .Skip(offset)
                .Take(limit))
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

        var today = Today;
        var accrualQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && garageIds.Contains(accrual.GarageId))
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                Category = AccrualBalanceCategory,
                GarageId = group.Key,
                AccrualAmount = group.Sum(accrual => accrual.Amount),
                IncomeAmount = 0m,
                OverdueAccrualAmount = group.Sum(accrual =>
                    !accrual.DueDateNeedsReview && accrual.OverdueFromDate <= today
                        ? accrual.Amount
                        : 0m),
                OverdueAccrualCount = group.Count(accrual =>
                    !accrual.DueDateNeedsReview && accrual.OverdueFromDate <= today),
                AllocatedIncomeAmount = 0m,
                OverdueAllocatedAmount = 0m
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
                AccrualAmount = 0m,
                IncomeAmount = group.Sum(operation => operation.Amount),
                OverdueAccrualAmount = 0m,
                OverdueAccrualCount = 0,
                AllocatedIncomeAmount = 0m,
                OverdueAllocatedAmount = 0m
            });
        var allocationQuery =
            from allocation in dbContext.AccrualPaymentAllocations.AsNoTracking()
            join accrual in dbContext.Accruals.AsNoTracking() on allocation.AccrualId equals accrual.Id
            join operation in dbContext.FinancialOperations.AsNoTracking()
                on allocation.FinancialOperationId equals operation.Id
            where allocation.IsActive &&
                  !accrual.IsCanceled &&
                  !operation.IsCanceled &&
                  garageIds.Contains(accrual.GarageId)
            group new { allocation.Amount, accrual.DueDateNeedsReview, accrual.OverdueFromDate }
                by accrual.GarageId
            into allocationGroup
            select new
            {
                Category = AllocationBalanceCategory,
                GarageId = allocationGroup.Key,
                AccrualAmount = 0m,
                IncomeAmount = 0m,
                OverdueAccrualAmount = 0m,
                OverdueAccrualCount = 0,
                AllocatedIncomeAmount = allocationGroup.Sum(item => item.Amount),
                OverdueAllocatedAmount = allocationGroup.Sum(item =>
                    !item.DueDateNeedsReview && item.OverdueFromDate <= today
                        ? item.Amount
                        : 0m)
            };
        var rows = await accrualQuery
            .Concat(incomeQuery)
            .Concat(allocationQuery)
            .ToListAsync(cancellationToken);
        var accrualTotals = rows
            .Where(row => row.Category == AccrualBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.AccrualAmount);
        var incomeTotals = rows
            .Where(row => row.Category == IncomeBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.IncomeAmount);
        var allocatedIncomeTotals = rows
            .Where(row => row.Category == AllocationBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.AllocatedIncomeAmount);
        var overdueAllocatedTotals = rows
            .Where(row => row.Category == AllocationBalanceCategory)
            .ToDictionary(row => row.GarageId, row => row.OverdueAllocatedAmount);
        var overdueAccrualTotals = rows
            .Where(row => row.Category == AccrualBalanceCategory && row.OverdueAccrualCount > 0)
            .ToDictionary(
                row => row.GarageId,
                row => row.OverdueAccrualAmount - overdueAllocatedTotals.GetValueOrDefault(row.GarageId));
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

    public async Task<IReadOnlyList<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken) =>
        await dbContext.Garages
            .AsNoTracking()
            .Where(garage => !garage.IsArchived)
            .Select(garage => garage.Id)
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
            .Where(garage => includeArchived || !garage.IsArchived);

    private static IQueryable<GarageListItemData> ProjectListItems(IQueryable<Garage> query) =>
        query.Select(garage => new GarageListItemData(
            garage.Id,
            garage.Number,
            garage.PeopleCount,
            garage.FloorCount,
            garage.OwnerId,
            garage.Owner == null
                ? null
                : (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).Trim(),
            garage.Owner == null ? null : garage.Owner.Phone,
            garage.StartingBalance,
            garage.InitialWaterMeterValue,
            garage.InitialElectricityMeterValue,
            garage.Comment,
            garage.IsArchived));

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

    private static IOrderedQueryable<Garage> ApplySearchRanking(IQueryable<Garage> query, string normalizedSearch) =>
        query
            .OrderBy(garage => garage.Number.ToLower() == normalizedSearch
                ? 0
                : garage.Number.ToLower().StartsWith(normalizedSearch)
                    ? 1
                    : garage.Number.ToLower().Contains(normalizedSearch)
                        ? 2
                        : 3)
            .ThenBy(garage => garage.Number.Length)
            .ThenBy(garage => garage.Number.ToLower());

    private static IOrderedEnumerable<GarageListItemData> ApplySearchRanking(
        IEnumerable<GarageListItemData> garages,
        string normalizedSearch) =>
        garages
            .OrderBy(garage => garage.Number.Equals(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                ? 0
                : garage.Number.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                        ? 2
                        : 3)
            .ThenBy(garage => garage.Number.Length)
            .ThenBy(garage => garage.Number, StringComparer.OrdinalIgnoreCase)
            .ThenBy(garage => garage.Id);

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
                (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && !accrual.DueDateNeedsReview && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
                (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.Accrual.DueDateNeedsReview && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
                0m)),
            ("overdueDebt", false) => query.OrderBy(garage => Math.Max(
                garage.StartingBalance +
                (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && !accrual.DueDateNeedsReview && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
                (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
                (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.Accrual.DueDateNeedsReview && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
                0m)),
            (_, true) => query.OrderByDescending(garage => garage.Number),
            _ => query.OrderBy(garage => garage.Number)
        };
    }

    private static IQueryable<Garage> ApplyColumnFilters(IQueryable<Garage> query, GarageColumnFilters filters)
    {
        if (filters.Number is { } number)
        {
            query = query.Where(garage => garage.Number.ToLower().Contains(number));
        }
        if (filters.PeopleCountMin is { } peopleCountMin)
        {
            query = query.Where(garage => garage.PeopleCount >= peopleCountMin);
        }
        if (filters.PeopleCountMax is { } peopleCountMax)
        {
            query = query.Where(garage => garage.PeopleCount <= peopleCountMax);
        }
        if (filters.FloorCountMin is { } floorCountMin)
        {
            query = query.Where(garage => garage.FloorCount >= floorCountMin);
        }
        if (filters.FloorCountMax is { } floorCountMax)
        {
            query = query.Where(garage => garage.FloorCount <= floorCountMax);
        }

        return query;
    }

    private static bool HasColumnFilters(GarageColumnFilters filters) =>
        filters.Number is not null ||
        filters.PeopleCountMin.HasValue ||
        filters.PeopleCountMax.HasValue ||
        filters.FloorCountMin.HasValue ||
        filters.FloorCountMax.HasValue;

    private static bool GarageMatchesColumnFilters(GarageListItemData garage, GarageColumnFilters filters) =>
        (filters.Number is null || garage.Number.Contains(filters.Number, StringComparison.OrdinalIgnoreCase)) &&
        (!filters.PeopleCountMin.HasValue || garage.PeopleCount >= filters.PeopleCountMin.Value) &&
        (!filters.PeopleCountMax.HasValue || garage.PeopleCount <= filters.PeopleCountMax.Value) &&
        (!filters.FloorCountMin.HasValue || garage.FloorCount >= filters.FloorCountMin.Value) &&
        (!filters.FloorCountMax.HasValue || garage.FloorCount <= filters.FloorCountMax.Value);

    private IQueryable<Garage> ApplyDebtorsFilter(IQueryable<Garage> query)
    {
        var today = Today;
        return query.Where(garage =>
            !garage.IsArchived &&
            garage.StartingBalance +
            (dbContext.Accruals.Where(accrual => accrual.GarageId == garage.Id && !accrual.IsCanceled && !accrual.DueDateNeedsReview && accrual.OverdueFromDate <= today).Sum(accrual => (decimal?)accrual.Amount) ?? 0m) -
            (dbContext.FinancialOperations.Where(operation => operation.GarageId == garage.Id && !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income).Sum(operation => (decimal?)operation.Amount) ?? 0m) +
            (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) -
            (dbContext.AccrualPaymentAllocations.Where(allocation => allocation.IsActive && allocation.Accrual.GarageId == garage.Id && !allocation.Accrual.IsCanceled && !allocation.Accrual.DueDateNeedsReview && allocation.Accrual.OverdueFromDate <= today && !allocation.FinancialOperation.IsCanceled).Sum(allocation => (decimal?)allocation.Amount) ?? 0m) > 0m);
    }

    private static IOrderedEnumerable<GarageListItemData> ApplySqlitePageSorting(
        IEnumerable<GarageListItemData> garages,
        GarageBalanceTotalsData totals,
        string sortBy,
        bool descending)
    {
        Func<GarageListItemData, decimal> overdueDebt = garage => CalculateOverdueDebt(garage, totals);

        var ordered = (sortBy, descending) switch
        {
            ("peopleCount", true) => garages.OrderByDescending(garage => garage.PeopleCount),
            ("peopleCount", false) => garages.OrderBy(garage => garage.PeopleCount),
            ("floorCount", true) => garages.OrderByDescending(garage => garage.FloorCount),
            ("floorCount", false) => garages.OrderBy(garage => garage.FloorCount),
            ("owner", true) => garages.OrderByDescending(garage => garage.OwnerName),
            ("owner", false) => garages.OrderBy(garage => garage.OwnerName),
            ("phone", true) => garages.OrderByDescending(garage => garage.OwnerPhone),
            ("phone", false) => garages.OrderBy(garage => garage.OwnerPhone),
            ("overdueDebt", true) => garages.OrderByDescending(overdueDebt),
            ("overdueDebt", false) => garages.OrderBy(overdueDebt),
            (_, true) => garages.OrderByDescending(garage => garage.Number),
            _ => garages.OrderBy(garage => garage.Number)
        };
        return ordered.ThenBy(garage => garage.Id);
    }

    private static decimal CalculateOverdueDebt(GarageListItemData garage, GarageBalanceTotalsData totals) =>
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

    private static bool GarageMatchesSearch(GarageListItemData garage, string normalizedSearch) =>
        garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (garage.OwnerName?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);
}
