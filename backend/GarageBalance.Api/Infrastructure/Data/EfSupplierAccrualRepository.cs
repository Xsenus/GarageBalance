using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfSupplierAccrualRepository(GarageBalanceDbContext dbContext) : ISupplierAccrualRepository
{
    public async Task<IReadOnlyList<SupplierAccrual>> GetListAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        Guid? supplierId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, supplierId);
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

    public async Task<SupplierAccrualPageData> GetPageAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        Guid? supplierId,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, supplierId);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            var filtered = (await Order(query).ToListAsync(cancellationToken))
                .Where(accrual => AccrualMatchesSearch(accrual, normalizedSearch))
                .ToList();
            return new SupplierAccrualPageData(filtered.Skip(offset).Take(limit).ToList(), filtered.Count);
        }

        query = ApplySearch(query, normalizedSearch);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new SupplierAccrualPageData(items, totalCount);
    }

    private IQueryable<SupplierAccrual> QueryActive() =>
        dbContext.SupplierAccruals.AsNoTracking()
            .Include(accrual => accrual.Supplier)
            .Include(accrual => accrual.ExpenseType)
            .Where(accrual => !accrual.IsCanceled);

    private static IQueryable<SupplierAccrual> ApplyFilters(
        IQueryable<SupplierAccrual> query,
        DateOnly? monthFrom,
        DateOnly? monthTo,
        Guid? supplierId)
    {
        if (monthFrom.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth >= monthFrom.Value);
        }

        if (monthTo.HasValue)
        {
            query = query.Where(accrual => accrual.AccountingMonth <= monthTo.Value);
        }

        if (supplierId.HasValue)
        {
            query = query.Where(accrual => accrual.SupplierId == supplierId.Value);
        }

        return query;
    }

    private static IQueryable<SupplierAccrual> ApplySearch(IQueryable<SupplierAccrual> query, string? normalizedSearch)
    {
        if (normalizedSearch is null)
        {
            return query;
        }

        return query.Where(accrual =>
            accrual.Supplier.Name.ToLower().Contains(normalizedSearch) ||
            accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch) ||
            (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
            (accrual.Comment != null && accrual.Comment.ToLower().Contains(normalizedSearch)));
    }

    private static IOrderedQueryable<SupplierAccrual> Order(IQueryable<SupplierAccrual> query) =>
        query.OrderByDescending(accrual => accrual.AccountingMonth)
            .ThenBy(accrual => accrual.Supplier.Name);

    private static bool AccrualMatchesSearch(SupplierAccrual accrual, string normalizedSearch) =>
        accrual.Supplier.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        accrual.ExpenseType.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
        (accrual.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accrual.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
