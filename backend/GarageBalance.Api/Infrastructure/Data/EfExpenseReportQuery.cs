using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseReportQuery(GarageBalanceDbContext dbContext) : IExpenseReportQuery
{
    private const int StartingBalanceTotalCategory = 1;
    private const int AccrualTotalCategory = 2;
    private const int ExpenseTotalCategory = 3;
    private const string AllRows = "all";
    private const string AccrualRows = "accruals";
    private const string PaymentRows = "payments";
    private const string StartingBalanceRows = "starting_balance";

    public async Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExpenseReportRowDto>();
        var accrualTotal = 0m;
        var expenseTotal = 0m;
        var rowCount = 0;
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var useClientSearch = hasSearch && !(dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false);
        var fetchLimit = useClientSearch ? null : GetFetchLimit(offset, limit);
        var aggregateQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });
        var hasAggregateQueries = false;

        if (rowMode is AllRows or AccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var startingBalanceQuery = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => !supplier.IsArchived && supplier.StartingBalance != 0);
                if (supplierIds.Count > 0)
                {
                    startingBalanceQuery = startingBalanceQuery.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                if (hasSearch && !useClientSearch && !"Стартовый баланс".Contains(normalizedSearch!, StringComparison.OrdinalIgnoreCase))
                {
                    startingBalanceQuery = startingBalanceQuery.Where(supplier => supplier.Name.ToLower().Contains(normalizedSearch!));
                }

                if (!useClientSearch)
                {
                    var startingBalanceAggregate = startingBalanceQuery
                        .GroupBy(_ => 1)
                        .Select(group => new
                        {
                            Category = StartingBalanceTotalCategory,
                            Total = group.Sum(supplier => supplier.StartingBalance),
                            Count = group.Count()
                        });
                    aggregateQuery = aggregateQuery.Concat(startingBalanceAggregate);
                    hasAggregateQueries = true;
                }

                var startingBalances = await ApplyLimit(startingBalanceQuery.OrderBy(supplier => supplier.Name).ThenBy(supplier => supplier.Id), fetchLimit)
                    .ToListAsync(cancellationToken);
                rows.AddRange(startingBalances.Select(supplier => new ExpenseReportRowDto(
                    StartingBalanceRows,
                    dateFrom,
                    dateFrom,
                    supplier.Id,
                    supplier.Name,
                    Guid.Empty,
                    "Стартовый баланс",
                    supplier.StartingBalance,
                    0m,
                    supplier.StartingBalance,
                    null,
                    "Начальное обязательство перед поставщиком")));
            }

            var accrualsQuery = dbContext.SupplierAccruals.AsNoTracking()
                .Include(accrual => accrual.Supplier)
                .Include(accrual => accrual.ExpenseType)
                .Where(accrual =>
                    !accrual.IsCanceled &&
                    accrual.AccountingMonth >= dateFrom &&
                    accrual.AccountingMonth <= dateTo);
            if (supplierIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accrualsQuery = accrualsQuery.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            if (hasSearch && !useClientSearch)
            {
                accrualsQuery = accrualsQuery.Where(accrual =>
                    accrual.Supplier.Name.ToLower().Contains(normalizedSearch!) ||
                    accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch!) ||
                    (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            if (!useClientSearch)
            {
                var accrualAggregate = accrualsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = AccrualTotalCategory,
                        Total = group.Sum(accrual => accrual.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(accrualAggregate);
                hasAggregateQueries = true;
            }

            var accruals = await ApplyLimit(
                    accrualsQuery.OrderBy(accrual => accrual.AccountingMonth).ThenBy(accrual => accrual.Supplier.Name).ThenBy(accrual => accrual.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            rows.AddRange(accruals.Select(accrual => new ExpenseReportRowDto(
                AccrualRows,
                accrual.AccountingMonth,
                accrual.AccountingMonth,
                accrual.SupplierId,
                accrual.Supplier.Name,
                accrual.ExpenseTypeId,
                accrual.ExpenseType.Name,
                accrual.Amount,
                0m,
                accrual.Amount,
                accrual.DocumentNumber,
                accrual.Comment)));
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var paymentsQuery = dbContext.FinancialOperations.AsNoTracking()
                .Include(operation => operation.Supplier)
                .Include(operation => operation.ExpenseType)
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.SupplierId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (supplierIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => supplierIds.Contains(operation.SupplierId!.Value));
            }

            if (expenseTypeIds.Count > 0)
            {
                paymentsQuery = paymentsQuery.Where(operation => expenseTypeIds.Contains(operation.ExpenseTypeId!.Value));
            }

            if (hasSearch && !useClientSearch)
            {
                paymentsQuery = paymentsQuery.Where(operation =>
                    operation.Supplier!.Name.ToLower().Contains(normalizedSearch!) ||
                    operation.ExpenseType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            if (!useClientSearch)
            {
                var expenseAggregate = paymentsQuery
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = ExpenseTotalCategory,
                        Total = group.Sum(operation => operation.Amount),
                        Count = group.Count()
                    });
                aggregateQuery = aggregateQuery.Concat(expenseAggregate);
                hasAggregateQueries = true;
            }

            var payments = await ApplyLimit(
                    paymentsQuery.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.Supplier!.Name).ThenBy(operation => operation.Id),
                    fetchLimit)
                .ToListAsync(cancellationToken);
            rows.AddRange(payments.Select(operation => new ExpenseReportRowDto(
                PaymentRows,
                operation.OperationDate,
                operation.AccountingMonth,
                operation.SupplierId!.Value,
                operation.Supplier!.Name,
                operation.ExpenseTypeId!.Value,
                operation.ExpenseType!.Name,
                0m,
                operation.Amount,
                -operation.Amount,
                operation.DocumentNumber,
                operation.Comment)));
        }

        if (!useClientSearch && hasAggregateQueries)
        {
            var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
            accrualTotal = aggregates
                .Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory)
                .Sum(row => row.Total);
            expenseTotal = aggregates
                .Where(row => row.Category == ExpenseTotalCategory)
                .Sum(row => row.Total);
            rowCount = aggregates.Sum(row => row.Count);
        }
        else if (useClientSearch)
        {
            var clientSearch = search!.Trim();
            rows = rows.Where(row =>
                    row.SupplierName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.ExpenseTypeName.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(clientSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            accrualTotal = rows.Sum(row => row.AccrualAmount);
            expenseTotal = rows.Sum(row => row.ExpenseAmount);
            rowCount = rows.Count;
        }

        var visibleRows = ApplyPage(
                rows.OrderBy(row => row.Date).ThenBy(row => row.SupplierName).ThenBy(row => row.RowType),
                offset,
                limit)
            .ToList();
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, visibleRows);
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private static int? GetFetchLimit(int offset, int? limit) =>
        limit is > 0 ? (int)Math.Min((long)offset + limit.Value, int.MaxValue) : null;
}
