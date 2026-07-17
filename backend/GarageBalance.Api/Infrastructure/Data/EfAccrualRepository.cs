using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualRepository(GarageBalanceDbContext dbContext) : IAccrualRepository
{
    public async Task<IReadOnlyList<Accrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyPeriod(QueryActive(), monthFrom, monthTo);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await Order(query).ToListAsync(cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .Take(limit)
                .ToList();
        }

        return await Order(ApplySearch(query, normalizedSearch))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccrualPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyPeriod(QueryActive(), monthFrom, monthTo);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .ToList();
            return new AccrualPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AccrualPageData(items, totalCount);
    }

    public async Task<AccrualPageData> GetDueDateReviewPageAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .Include(accrual => accrual.IncomeType)
            .Where(accrual => !accrual.IsCanceled && accrual.DueDateNeedsReview);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number)
            .ThenBy(accrual => accrual.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new AccrualPageData(items, totalCount);
    }

    public async Task<decimal> GetTotalBeforeMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth < accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public async Task<IReadOnlyList<OverdueAccrualDebtData>> GetOverdueDebtDetailsAsync(
        Guid garageId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                !accrual.DueDateNeedsReview &&
                accrual.GarageId == garageId &&
                accrual.OverdueFromDate <= asOfDate)
            .Select(accrual => new
            {
                AccrualId = accrual.Id,
                accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                accrual.AccountingMonth,
                accrual.DueDate,
                accrual.OverdueFromDate,
                accrual.Amount,
                PaidAmount = dbContext.AccrualPaymentAllocations
                    .Where(allocation =>
                        allocation.IsActive &&
                        allocation.AccrualId == accrual.Id &&
                        !allocation.FinancialOperation.IsCanceled)
                    .Sum(allocation => (decimal?)allocation.Amount) ?? 0m
            });

        var rows = await query.ToListAsync(cancellationToken);
        return rows
            .Select(row => new OverdueAccrualDebtData(
                row.AccrualId,
                row.IncomeTypeId,
                row.IncomeTypeName,
                row.AccountingMonth,
                row.DueDate,
                row.OverdueFromDate,
                row.Amount,
                row.PaidAmount,
                Math.Max(row.Amount - row.PaidAmount, 0m)))
            .Where(row => row.OutstandingAmount > 0m)
            .OrderBy(row => row.OverdueFromDate)
            .ThenBy(row => row.DueDate)
            .ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.IncomeTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.AccrualId)
            .ToList();
    }

    public async Task<IReadOnlyList<AccrualBucketData>> GetMonthlyBucketsAsync(
        Guid garageId,
        DateOnly? monthFrom,
        DateOnly monthTo,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= monthTo);
        if (monthFrom.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        var rows = await query
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .OrderBy(bucket => bucket.AccountingMonth)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new AccrualBucketData(row.AccountingMonth, row.Amount)).ToList();
    }

    public Task<Accrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        TrackedAggregate().SingleOrDefaultAsync(accrual => accrual.Id == id, cancellationToken);

    public Task<Accrual?> FindActiveForUpdateAsync(
        Guid garageId,
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        TrackedAggregate().SingleOrDefaultAsync(accrual =>
            !accrual.IsCanceled &&
            accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source,
            cancellationToken);

    public async Task<IReadOnlyList<Accrual>> GetActiveMeteredForUpdateAsync(
        Guid garageId,
        DateOnly accountingMonth,
        string meterKind,
        CancellationToken cancellationToken)
    {
        var calculationBase = meterKind switch
        {
            MeterKinds.Water => TariffCalculationBases.MeterWater,
            MeterKinds.Electricity => TariffCalculationBases.MeterElectricity,
            _ => null
        };
        if (calculationBase is null)
        {
            return [];
        }

        return await TrackedAggregate()
            .Include(accrual => accrual.Tariff)
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.GarageId == garageId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.Source == AccrualSources.Regular &&
                accrual.Tariff != null &&
                accrual.Tariff.CalculationBase == calculationBase)
            .OrderBy(accrual => accrual.IncomeTypeId)
            .ThenBy(accrual => accrual.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetActiveGarageIdsAsync(
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.IncomeTypeId == incomeTypeId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.Source == source)
            .Select(accrual => accrual.GarageId)
            .ToHashSetAsync(cancellationToken);

    public Task<int> CountActiveForGenerationAsync(
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().CountAsync(accrual =>
            !accrual.IsCanceled &&
            !accrual.Garage.IsArchived &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source,
            cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(
        Guid? ignoredId,
        Guid garageId,
        Guid incomeTypeId,
        DateOnly accountingMonth,
        string source,
        CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.GarageId == garageId &&
            accrual.IncomeTypeId == incomeTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source,
            cancellationToken);

    public async Task<decimal> GetTotalThroughMonthAsync(Guid garageId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.GarageId == garageId && accrual.AccountingMonth <= accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public void Add(Accrual accrual) => dbContext.Accruals.Add(accrual);

    private IQueryable<Accrual> QueryActive() =>
        dbContext.Accruals.AsNoTracking()
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(accrual => accrual.IncomeType)
            .Where(accrual => !accrual.IsCanceled);

    private IQueryable<Accrual> TrackedAggregate() =>
        dbContext.Accruals
            .Include(accrual => accrual.Garage)
            .ThenInclude(garage => garage.Owner)
            .Include(accrual => accrual.IncomeType);

    private static IQueryable<Accrual> ApplyPeriod(IQueryable<Accrual> query, DateOnly? monthFrom, DateOnly? monthTo)
    {
        if (monthFrom.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
        }

        return query;
    }

    private static IQueryable<Accrual> ApplySearch(IQueryable<Accrual> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(accrual =>
            accrual.Garage.Number.ToLower().Contains(normalizedSearch) ||
            accrual.IncomeType.Name.ToLower().Contains(normalizedSearch) ||
            (accrual.Comment != null && accrual.Comment.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<Accrual> Order(IQueryable<Accrual> query) =>
        query.OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Garage.Number);

    private static bool AccrualMatchesSearch(Accrual accrual, string normalizedSearch) =>
        accrual.Garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.IncomeType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
