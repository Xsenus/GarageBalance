using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseReportQuery(GarageBalanceDbContext dbContext) : IExpenseReportQuery
{
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
        CancellationToken cancellationToken)
    {
        var rows = new List<ExpenseReportRowDto>();
        var accrualTotal = 0m;
        var expenseTotal = 0m;
        var rowCount = 0;
        var hasSearch = !string.IsNullOrWhiteSpace(search);

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

                if (!hasSearch)
                {
                    accrualTotal += await startingBalanceQuery.SumAsync(supplier => supplier.StartingBalance, cancellationToken);
                    rowCount += await startingBalanceQuery.CountAsync(cancellationToken);
                }

                var startingBalances = await ApplyLimit(startingBalanceQuery.OrderBy(supplier => supplier.Name), hasSearch ? null : limit)
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

            if (!hasSearch)
            {
                accrualTotal += await accrualsQuery.SumAsync(accrual => accrual.Amount, cancellationToken);
                rowCount += await accrualsQuery.CountAsync(cancellationToken);
            }

            var accruals = await ApplyLimit(
                    accrualsQuery.OrderBy(accrual => accrual.AccountingMonth).ThenBy(accrual => accrual.Supplier.Name),
                    hasSearch ? null : limit)
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

            if (!hasSearch)
            {
                expenseTotal += await paymentsQuery.SumAsync(operation => operation.Amount, cancellationToken);
                rowCount += await paymentsQuery.CountAsync(cancellationToken);
            }

            var payments = await ApplyLimit(
                    paymentsQuery.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.Supplier!.Name),
                    hasSearch ? null : limit)
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

        if (hasSearch)
        {
            var normalizedSearch = search!.Trim();
            rows = rows.Where(row =>
                    row.SupplierName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.ExpenseTypeName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    (row.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
            accrualTotal = rows.Sum(row => row.AccrualAmount);
            expenseTotal = rows.Sum(row => row.ExpenseAmount);
            rowCount = rows.Count;
        }

        var visibleRows = ApplyLimit(
                rows.OrderBy(row => row.Date).ThenBy(row => row.SupplierName).ThenBy(row => row.RowType),
                limit)
            .ToList();
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, visibleRows);
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> rows, int? limit) =>
        limit is > 0 ? rows.Take(limit.Value) : rows;
}
