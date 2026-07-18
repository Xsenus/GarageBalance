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
        ReportSort sort,
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
            var ordered = ApplyCashPaymentSort(query, sort).ThenByDescending(operation => operation.Id);
            var operations = await ApplyPage(ordered, offset, limit).ToListAsync(cancellationToken);
            return new CashPaymentReportData(operations, total, rowCount);
        }

        var fallbackOperations = await query.ToListAsync(cancellationToken);
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

        var orderedFallbackOperations = ApplyCashPaymentSort(fallbackOperations, sort)
            .ThenByDescending(operation => operation.Id)
            .ToList();
        return new CashPaymentReportData(
            ApplyPage(orderedFallbackOperations, offset, limit).ToList(),
            fallbackOperations.Sum(operation => operation.Amount),
            fallbackOperations.Count);
    }

    public async Task<BankDepositReportData> GetBankDepositsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        ReportSort sort,
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

            operations = ApplyBankDepositSort(operations, sort).ThenByDescending(operation => operation.Id).ToList();
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
        var orderedQuery = ApplyBankDepositSort(query, sort).ThenByDescending(operation => operation.Id);
        var result = await ApplyPage(orderedQuery, offset, limit).ToListAsync(cancellationToken);
        return new BankDepositReportData(result, total, rowCount);
    }

    private static IOrderedQueryable<FinancialOperation> ApplyCashPaymentSort(IQueryable<FinancialOperation> query, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? query.OrderByDescending(operation => operation.Amount) : query.OrderBy(operation => operation.Amount),
            "hasReceipt" => sort.Descending
                ? query.OrderByDescending(operation => operation.DocumentNumber != null && operation.DocumentNumber != string.Empty)
                : query.OrderBy(operation => operation.DocumentNumber != null && operation.DocumentNumber != string.Empty),
            "purpose" => sort.Descending
                ? query.OrderByDescending(operation => (operation.ExpenseType == null ? string.Empty : operation.ExpenseType.Name) + ": " + (operation.Supplier == null ? string.Empty : operation.Supplier.Name))
                : query.OrderBy(operation => (operation.ExpenseType == null ? string.Empty : operation.ExpenseType.Name) + ": " + (operation.Supplier == null ? string.Empty : operation.Supplier.Name)),
            "supplierName" => sort.Descending
                ? query.OrderByDescending(operation => operation.Supplier == null ? string.Empty : operation.Supplier.Name)
                : query.OrderBy(operation => operation.Supplier == null ? string.Empty : operation.Supplier.Name),
            "expenseTypeName" => sort.Descending
                ? query.OrderByDescending(operation => operation.ExpenseType == null ? string.Empty : operation.ExpenseType.Name)
                : query.OrderBy(operation => operation.ExpenseType == null ? string.Empty : operation.ExpenseType.Name),
            "documentNumber" => sort.Descending ? query.OrderByDescending(operation => operation.DocumentNumber) : query.OrderBy(operation => operation.DocumentNumber),
            _ => sort.Descending ? query.OrderByDescending(operation => operation.OperationDate) : query.OrderBy(operation => operation.OperationDate)
        };

    private static IOrderedEnumerable<FinancialOperation> ApplyCashPaymentSort(IEnumerable<FinancialOperation> operations, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? operations.OrderByDescending(operation => operation.Amount) : operations.OrderBy(operation => operation.Amount),
            "hasReceipt" => sort.Descending ? operations.OrderByDescending(operation => !string.IsNullOrWhiteSpace(operation.DocumentNumber)) : operations.OrderBy(operation => !string.IsNullOrWhiteSpace(operation.DocumentNumber)),
            "purpose" => sort.Descending ? operations.OrderByDescending(BuildCashPaymentPurpose, StringComparer.Ordinal) : operations.OrderBy(BuildCashPaymentPurpose, StringComparer.Ordinal),
            "supplierName" => sort.Descending ? operations.OrderByDescending(operation => operation.Supplier?.Name, StringComparer.Ordinal) : operations.OrderBy(operation => operation.Supplier?.Name, StringComparer.Ordinal),
            "expenseTypeName" => sort.Descending ? operations.OrderByDescending(operation => operation.ExpenseType?.Name, StringComparer.Ordinal) : operations.OrderBy(operation => operation.ExpenseType?.Name, StringComparer.Ordinal),
            "documentNumber" => sort.Descending ? operations.OrderByDescending(operation => operation.DocumentNumber, StringComparer.Ordinal) : operations.OrderBy(operation => operation.DocumentNumber, StringComparer.Ordinal),
            _ => sort.Descending ? operations.OrderByDescending(operation => operation.OperationDate) : operations.OrderBy(operation => operation.OperationDate)
        };

    private static IOrderedQueryable<FundOperation> ApplyBankDepositSort(IQueryable<FundOperation> query, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? query.OrderByDescending(operation => operation.Amount) : query.OrderBy(operation => operation.Amount),
            "fundName" => sort.Descending ? query.OrderByDescending(operation => operation.Fund.Name) : query.OrderBy(operation => operation.Fund.Name),
            "comment" => sort.Descending ? query.OrderByDescending(operation => operation.Reason) : query.OrderBy(operation => operation.Reason),
            _ => sort.Descending ? query.OrderByDescending(operation => operation.CreatedAtUtc) : query.OrderBy(operation => operation.CreatedAtUtc)
        };

    private static IOrderedEnumerable<FundOperation> ApplyBankDepositSort(IEnumerable<FundOperation> operations, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? operations.OrderByDescending(operation => operation.Amount) : operations.OrderBy(operation => operation.Amount),
            "fundName" => sort.Descending ? operations.OrderByDescending(operation => operation.Fund.Name, StringComparer.Ordinal) : operations.OrderBy(operation => operation.Fund.Name, StringComparer.Ordinal),
            "comment" => sort.Descending ? operations.OrderByDescending(operation => operation.Reason, StringComparer.Ordinal) : operations.OrderBy(operation => operation.Reason, StringComparer.Ordinal),
            _ => sort.Descending ? operations.OrderByDescending(operation => operation.CreatedAtUtc) : operations.OrderBy(operation => operation.CreatedAtUtc)
        };

    private static string BuildCashPaymentPurpose(FinancialOperation operation)
    {
        var parts = new[] { operation.ExpenseType?.Name, operation.Supplier?.Name }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var purpose = string.Join(": ", parts);
        return string.IsNullOrWhiteSpace(purpose) ? operation.Comment ?? "Оплата из кассы" : purpose;
    }

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
