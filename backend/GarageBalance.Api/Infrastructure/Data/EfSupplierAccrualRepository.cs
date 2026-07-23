using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Dictionaries;
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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await Order(query)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return new SupplierAccrualPageData(items, totalCount);
    }

    private async Task<SupplierAccrualPageData> GetPostgresPageAsync(
        IQueryable<SupplierAccrual> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = Order(query)
            .Skip(offset)
            .Take(limit)
            .Select(accrual => new
            {
                Category = PageCategory,
                Id = (Guid?)accrual.Id,
                SupplierId = (Guid?)accrual.SupplierId,
                SupplierName = (string?)accrual.Supplier.Name,
                ExpenseTypeId = (Guid?)accrual.ExpenseTypeId,
                ExpenseTypeName = (string?)accrual.ExpenseType.Name,
                AccountingMonth = (DateOnly?)accrual.AccountingMonth,
                Amount = (decimal?)accrual.Amount,
                Source = (string?)accrual.Source,
                accrual.DocumentNumber,
                accrual.Comment,
                IsCanceled = (bool?)accrual.IsCanceled,
                CreatedAtUtc = (DateTimeOffset?)accrual.CreatedAtUtc,
                UpdatedAtUtc = (DateTimeOffset?)accrual.UpdatedAtUtc,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                SupplierId = (Guid?)null,
                SupplierName = (string?)null,
                ExpenseTypeId = (Guid?)null,
                ExpenseTypeName = (string?)null,
                AccountingMonth = (DateOnly?)null,
                Amount = (decimal?)null,
                Source = (string?)null,
                DocumentNumber = (string?)null,
                Comment = (string?)null,
                IsCanceled = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                UpdatedAtUtc = (DateTimeOffset?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.AccountingMonth)
            .ThenBy(row => row.SupplierName)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new SupplierAccrual
            {
                Id = row.Id!.Value,
                SupplierId = row.SupplierId!.Value,
                Supplier = new Supplier { Id = row.SupplierId.Value, Name = row.SupplierName! },
                ExpenseTypeId = row.ExpenseTypeId!.Value,
                ExpenseType = new ExpenseType { Id = row.ExpenseTypeId.Value, Name = row.ExpenseTypeName! },
                AccountingMonth = row.AccountingMonth!.Value,
                Amount = row.Amount!.Value,
                Source = row.Source!,
                DocumentNumber = row.DocumentNumber,
                Comment = row.Comment,
                IsCanceled = row.IsCanceled!.Value,
                CreatedAtUtc = row.CreatedAtUtc!.Value,
                UpdatedAtUtc = row.UpdatedAtUtc!.Value
            })
            .ToList();
        return new SupplierAccrualPageData(items, totalCount);
    }

    public async Task<int> CountActiveAsync(
        DateOnly? monthFrom,
        DateOnly? monthTo,
        string? normalizedSearch,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilters(QueryActive(), monthFrom, monthTo, supplierId: null);
        if (normalizedSearch is not null && IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .Count(accrual => AccrualMatchesSearch(accrual, normalizedSearch));
        }

        return await ApplySearch(query, normalizedSearch).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetActiveSupplierIdsAsync(
        Guid expenseTypeId,
        DateOnly accountingMonth,
        string source,
        string? documentNumber,
        CancellationToken cancellationToken) =>
        await dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.ExpenseTypeId == expenseTypeId &&
                accrual.AccountingMonth == accountingMonth &&
                accrual.Source == source &&
                accrual.DocumentNumber == documentNumber)
            .Select(accrual => accrual.SupplierId)
            .ToHashSetAsync(cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(
        Guid? ignoredId,
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly accountingMonth,
        string source,
        string? documentNumber,
        CancellationToken cancellationToken) =>
        dbContext.SupplierAccruals.AsNoTracking().AnyAsync(accrual =>
            !accrual.IsCanceled &&
            (!ignoredId.HasValue || accrual.Id != ignoredId.Value) &&
            accrual.SupplierId == supplierId &&
            accrual.ExpenseTypeId == expenseTypeId &&
            accrual.AccountingMonth == accountingMonth &&
            accrual.Source == source &&
            accrual.DocumentNumber == documentNumber,
            cancellationToken);

    public Task<SupplierAccrual?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.SupplierAccruals
            .Include(accrual => accrual.Supplier)
            .Include(accrual => accrual.ExpenseType)
            .SingleOrDefaultAsync(accrual => accrual.Id == id, cancellationToken);

    public Task<SupplierAccrual?> FindBySourceFinancialOperationForUpdateAsync(Guid operationId, CancellationToken cancellationToken) =>
        dbContext.SupplierAccruals
            .Include(accrual => accrual.Supplier)
            .Include(accrual => accrual.ExpenseType)
            .SingleOrDefaultAsync(accrual => accrual.SourceFinancialOperationId == operationId, cancellationToken);

    public async Task<decimal> GetTotalThroughMonthAsync(Guid supplierId, Guid expenseTypeId, DateOnly accountingMonth, CancellationToken cancellationToken) =>
        await dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.SupplierId == supplierId &&
                accrual.ExpenseTypeId == expenseTypeId &&
                accrual.AccountingMonth <= accountingMonth)
            .SumAsync(accrual => accrual.Amount, cancellationToken);

    public async Task<IReadOnlyList<SupplierAccrualBucketData>> GetMonthlyBucketsThroughMonthAsync(
        Guid supplierId,
        Guid expenseTypeId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.SupplierAccruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                accrual.SupplierId == supplierId &&
                accrual.ExpenseTypeId == expenseTypeId &&
                accrual.AccountingMonth <= accountingMonth)
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new { AccountingMonth = group.Key, Amount = group.Sum(accrual => accrual.Amount) })
            .OrderBy(bucket => bucket.AccountingMonth)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new SupplierAccrualBucketData(row.AccountingMonth, row.Amount)).ToList();
    }

    public void Add(SupplierAccrual accrual) => dbContext.SupplierAccruals.Add(accrual);

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
