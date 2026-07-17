using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfCashMovementReportQuery(GarageBalanceDbContext dbContext) : ICashMovementReportQuery
{
    public async Task<CashPaymentReportData> GetCashPaymentsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Supplier)
            .Include(operation => operation.ExpenseType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.OperationDate >= dateFrom &&
                operation.OperationDate <= dateTo);

        if (dbContext.Database.IsNpgsql())
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(operation =>
                    (operation.Supplier != null && operation.Supplier.Name.ToLower().Contains(normalizedSearch)) ||
                    (operation.ExpenseType != null && operation.ExpenseType.Name.ToLower().Contains(normalizedSearch)) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch)) ||
                    (operation.Comment != null && operation.Comment.ToLower().Contains(normalizedSearch)));
            }

            var totals = await query
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    RowCount = group.Count(),
                    Total = group.Sum(operation => operation.Amount)
                })
                .SingleOrDefaultAsync(cancellationToken);
            var rowCount = totals?.RowCount ?? 0;
            var total = totals?.Total ?? 0m;
            var ordered = query.OrderBy(operation => operation.OperationDate).ThenBy(operation => operation.DocumentNumber).ThenBy(operation => operation.Id);
            var operations = await ApplyPage(ordered, offset, limit).ToListAsync(cancellationToken);
            return new CashPaymentReportData(operations, total, rowCount);
        }

        var fallbackOperations = await query
            .OrderBy(operation => operation.OperationDate)
            .ThenBy(operation => operation.DocumentNumber)
            .ThenBy(operation => operation.Id)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            fallbackOperations = fallbackOperations.Where(operation =>
                    (operation.Supplier?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.ExpenseType?.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.DocumentNumber?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (operation.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return new CashPaymentReportData(
            ApplyPage(fallbackOperations, offset, limit).ToList(),
            fallbackOperations.Sum(operation => operation.Amount),
            fallbackOperations.Count);
    }

    public async Task<BankDepositReportData> GetBankDepositsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(dateFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toExclusiveUtc = new DateTimeOffset(dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var query = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FundOperationKinds.Deposit &&
                operation.IsCashToBankTransfer &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        if (IsSqliteProvider())
        {
            var operations = (await dbContext.FundOperations.AsNoTracking()
                    .Include(operation => operation.Fund)
                    .ToListAsync(cancellationToken))
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FundOperationKinds.Deposit &&
                    operation.IsCashToBankTransfer &&
                    operation.CreatedAtUtc >= fromUtc &&
                    operation.CreatedAtUtc < toExclusiveUtc)
                .ToList();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim();
                operations = operations.Where(operation =>
                        operation.Fund.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        operation.Reason.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            operations = operations.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Fund.Name).ThenBy(operation => operation.Id).ToList();
            return new BankDepositReportData(
                ApplyPage(operations, offset, limit).ToList(),
                operations.Sum(operation => operation.Amount),
                operations.Count);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(operation =>
                operation.Fund.Name.ToLower().Contains(normalizedSearch) ||
                operation.Reason.ToLower().Contains(normalizedSearch));
        }

        var totals = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                RowCount = group.Count(),
                Total = group.Sum(operation => operation.Amount)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var rowCount = totals?.RowCount ?? 0;
        var total = totals?.Total ?? 0m;
        var orderedQuery = query.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Fund.Name).ThenBy(operation => operation.Id);
        var result = await ApplyPage(orderedQuery, offset, limit).ToListAsync(cancellationToken);
        return new BankDepositReportData(result, total, rowCount);
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> items, int? limit) =>
        limit is > 0 ? items.Take(limit.Value) : items;

    private static IQueryable<T> ApplyPage<T>(IQueryable<T> query, int offset, int? limit)
    {
        var page = offset > 0 ? query.Skip(offset) : query;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> items, int offset, int? limit)
    {
        var page = offset > 0 ? items.Skip(offset) : items;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
