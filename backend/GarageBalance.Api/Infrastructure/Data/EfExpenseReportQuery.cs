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

    public Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        CancellationToken cancellationToken) =>
        GetRowsAsync(dateFrom, dateTo, rowMode, supplierIds, expenseTypeIds, search, limit, offset, new ReportSort("date", false), cancellationToken);

    public async Task<ExpenseReportQueryData> GetRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        if (IsNpgsql())
        {
            return await GetPostgresRowsAsync(
                dateFrom,
                dateTo,
                rowMode,
                supplierIds,
                expenseTypeIds,
                search,
                limit,
                offset,
                sort,
                cancellationToken);
        }

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
                ApplySort(rows, sort)
                    .ThenBy(row => row.SupplierName, StringComparer.Ordinal)
                    .ThenBy(row => row.ExpenseTypeName, StringComparer.Ordinal)
                    .ThenBy(row => row.SupplierId),
                offset,
                limit)
            .ToList();
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, visibleRows);
    }

    private async Task<ExpenseReportQueryData> GetPostgresRowsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string rowMode,
        IReadOnlySet<Guid> supplierIds,
        IReadOnlySet<Guid> expenseTypeIds,
        string? search,
        int? limit,
        int offset,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        IQueryable<ExpenseReportProjection>? reportRows = null;
        var aggregateQuery = dbContext.SupplierAccruals.AsNoTracking()
            .Where(_ => false)
            .Select(_ => new { Category = 0, Total = 0m, Count = 0 });

        if (rowMode is AllRows or AccrualRows)
        {
            if (expenseTypeIds.Count == 0)
            {
                var startingBalances = dbContext.Suppliers.AsNoTracking()
                    .Where(supplier => !supplier.IsArchived && supplier.StartingBalance != 0);
                if (supplierIds.Count > 0)
                {
                    startingBalances = startingBalances.Where(supplier => supplierIds.Contains(supplier.Id));
                }

                if (hasSearch && !"Стартовый баланс".Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    startingBalances = startingBalances.Where(supplier => supplier.Name.ToLower().Contains(normalizedSearch!));
                }

                aggregateQuery = aggregateQuery.Concat(startingBalances
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        Category = StartingBalanceTotalCategory,
                        Total = group.Sum(supplier => supplier.StartingBalance),
                        Count = group.Count()
                    }));

                reportRows = startingBalances.Select(supplier => new ExpenseReportProjection
                {
                    RowType = StartingBalanceRows,
                    Date = dateFrom,
                    AccountingMonth = dateFrom,
                    SupplierId = supplier.Id,
                    SupplierName = supplier.Name,
                    ExpenseTypeId = Guid.Empty,
                    ExpenseTypeName = "Стартовый баланс",
                    AccrualAmount = supplier.StartingBalance,
                    ExpenseAmount = 0m,
                    Difference = supplier.StartingBalance,
                    DocumentNumber = null,
                    Comment = "Начальное обязательство перед поставщиком",
                    CreatedAtUtc = supplier.CreatedAtUtc,
                    RowId = supplier.Id
                });
            }

            var accruals = dbContext.SupplierAccruals.AsNoTracking()
                .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= dateFrom && accrual.AccountingMonth <= dateTo);
            if (supplierIds.Count > 0)
            {
                accruals = accruals.Where(accrual => supplierIds.Contains(accrual.SupplierId));
            }

            if (expenseTypeIds.Count > 0)
            {
                accruals = accruals.Where(accrual => expenseTypeIds.Contains(accrual.ExpenseTypeId));
            }

            if (hasSearch)
            {
                accruals = accruals.Where(accrual =>
                    accrual.Supplier.Name.ToLower().Contains(normalizedSearch!) ||
                    accrual.ExpenseType.Name.ToLower().Contains(normalizedSearch!) ||
                    (accrual.DocumentNumber != null && accrual.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            aggregateQuery = aggregateQuery.Concat(accruals
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = AccrualTotalCategory,
                    Total = group.Sum(accrual => accrual.Amount),
                    Count = group.Count()
                }));

            var projectedAccruals = accruals.Select(accrual => new ExpenseReportProjection
            {
                RowType = AccrualRows,
                Date = accrual.AccountingMonth,
                AccountingMonth = accrual.AccountingMonth,
                SupplierId = accrual.SupplierId,
                SupplierName = accrual.Supplier.Name,
                ExpenseTypeId = accrual.ExpenseTypeId,
                ExpenseTypeName = accrual.ExpenseType.Name,
                AccrualAmount = accrual.Amount,
                ExpenseAmount = 0m,
                Difference = accrual.Amount,
                DocumentNumber = accrual.DocumentNumber,
                Comment = accrual.Comment,
                CreatedAtUtc = accrual.CreatedAtUtc,
                RowId = accrual.Id
            });
            reportRows = reportRows == null ? projectedAccruals : reportRows.Concat(projectedAccruals);
        }

        if (rowMode is AllRows or PaymentRows)
        {
            var payments = dbContext.FinancialOperations.AsNoTracking()
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FinancialOperationKinds.Expense &&
                    operation.SupplierId != null &&
                    operation.ExpenseTypeId != null &&
                    operation.OperationDate >= dateFrom &&
                    operation.OperationDate <= dateTo);
            if (supplierIds.Count > 0)
            {
                payments = payments.Where(operation => supplierIds.Contains(operation.SupplierId!.Value));
            }

            if (expenseTypeIds.Count > 0)
            {
                payments = payments.Where(operation => expenseTypeIds.Contains(operation.ExpenseTypeId!.Value));
            }

            if (hasSearch)
            {
                payments = payments.Where(operation =>
                    operation.Supplier!.Name.ToLower().Contains(normalizedSearch!) ||
                    operation.ExpenseType!.Name.ToLower().Contains(normalizedSearch!) ||
                    (operation.DocumentNumber != null && operation.DocumentNumber.ToLower().Contains(normalizedSearch!)));
            }

            aggregateQuery = aggregateQuery.Concat(payments
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Category = ExpenseTotalCategory,
                    Total = group.Sum(operation => operation.Amount),
                    Count = group.Count()
                }));

            var projectedPayments = payments.Select(operation => new ExpenseReportProjection
            {
                RowType = PaymentRows,
                Date = operation.OperationDate,
                AccountingMonth = operation.AccountingMonth,
                SupplierId = operation.SupplierId!.Value,
                SupplierName = operation.Supplier!.Name,
                ExpenseTypeId = operation.ExpenseTypeId!.Value,
                ExpenseTypeName = operation.ExpenseType!.Name,
                AccrualAmount = 0m,
                ExpenseAmount = operation.Amount,
                Difference = -operation.Amount,
                DocumentNumber = operation.DocumentNumber,
                Comment = operation.Comment,
                CreatedAtUtc = operation.CreatedAtUtc,
                RowId = operation.Id
            });
            reportRows = reportRows == null ? projectedPayments : reportRows.Concat(projectedPayments);
        }

        if (reportRows == null)
        {
            return new ExpenseReportQueryData(0m, 0m, 0, []);
        }

        var aggregates = await aggregateQuery.ToListAsync(cancellationToken);
        var rowCount = aggregates.Sum(row => row.Count);
        if (rowCount == 0)
        {
            return new ExpenseReportQueryData(0m, 0m, 0, []);
        }
        var accrualTotal = aggregates.Where(row => row.Category is StartingBalanceTotalCategory or AccrualTotalCategory).Sum(row => row.Total);
        var expenseTotal = aggregates.Where(row => row.Category == ExpenseTotalCategory).Sum(row => row.Total);

        var sortableRows = reportRows.Select(row => new ExpenseReportSortableProjection
        {
            RowType = row.RowType,
            Date = row.Date,
            AccountingMonth = row.AccountingMonth,
            SupplierId = row.SupplierId,
            SupplierName = row.SupplierName,
            ExpenseTypeId = row.ExpenseTypeId,
            ExpenseTypeName = row.ExpenseTypeName,
            AccrualAmount = row.AccrualAmount,
            ExpenseAmount = row.ExpenseAmount,
            Difference = row.Difference,
            DocumentNumber = row.DocumentNumber,
            Comment = row.Comment,
            CreatedAtUtc = row.CreatedAtUtc,
            RowId = row.RowId
        });
        var ordered = ApplyPostgresSort(sortableRows, sort)
            .ThenBy(row => row.SupplierName)
            .ThenBy(row => row.ExpenseTypeName)
            .ThenBy(row => row.RowId);
        IQueryable<ExpenseReportSortableProjection> page = offset > 0 ? ordered.Skip(offset) : ordered;
        if (limit is > 0)
        {
            page = page.Take(limit.Value);
        }

        var rows = await page
            .Select(row => new ExpenseReportRowDto(
                row.RowType,
                row.Date,
                row.AccountingMonth,
                row.SupplierId,
                row.SupplierName,
                row.ExpenseTypeId,
                row.ExpenseTypeName,
                row.AccrualAmount,
                row.ExpenseAmount,
                row.Difference,
                row.DocumentNumber,
                row.Comment))
            .ToListAsync(cancellationToken);
        return new ExpenseReportQueryData(accrualTotal, expenseTotal, rowCount, rows);
    }

    private static IOrderedQueryable<ExpenseReportSortableProjection> ApplyPostgresSort(IQueryable<ExpenseReportSortableProjection> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "supplierName" => sort.Descending ? rows.OrderByDescending(row => row.SupplierName) : rows.OrderBy(row => row.SupplierName),
            "expenseTypeName" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseTypeName) : rows.OrderBy(row => row.ExpenseTypeName),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "expenseAmount" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseAmount) : rows.OrderBy(row => row.ExpenseAmount),
            "difference" => sort.Descending ? rows.OrderByDescending(row => row.Difference) : rows.OrderBy(row => row.Difference),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber) : rows.OrderBy(row => row.DocumentNumber),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyPage<T>(IEnumerable<T> rows, int offset, int? limit)
    {
        var page = offset > 0 ? rows.Skip(offset) : rows;
        return limit is > 0 ? page.Take(limit.Value) : page;
    }

    private static int? GetFetchLimit(int offset, int? limit) =>
        limit is > 0 ? (int)Math.Min((long)offset + limit.Value, int.MaxValue) : null;

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static IOrderedEnumerable<ExpenseReportRowDto> ApplySort(IEnumerable<ExpenseReportRowDto> rows, ReportSort sort) =>
        sort.Field switch
        {
            "accountingMonth" => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth),
            "supplierName" => sort.Descending ? rows.OrderByDescending(row => row.SupplierName, StringComparer.Ordinal) : rows.OrderBy(row => row.SupplierName, StringComparer.Ordinal),
            "expenseTypeName" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseTypeName, StringComparer.Ordinal) : rows.OrderBy(row => row.ExpenseTypeName, StringComparer.Ordinal),
            "accrualAmount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualAmount) : rows.OrderBy(row => row.AccrualAmount),
            "expenseAmount" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseAmount) : rows.OrderBy(row => row.ExpenseAmount),
            "difference" => sort.Descending ? rows.OrderByDescending(row => row.Difference) : rows.OrderBy(row => row.Difference),
            "documentNumber" => sort.Descending ? rows.OrderByDescending(row => row.DocumentNumber, StringComparer.Ordinal) : rows.OrderBy(row => row.DocumentNumber, StringComparer.Ordinal),
            _ => sort.Descending ? rows.OrderByDescending(row => row.Date) : rows.OrderBy(row => row.Date)
        };

    private sealed class ExpenseReportProjection : ExpenseReportSortableProjection
    {
    }

    private class ExpenseReportSortableProjection
    {
        public string RowType { get; init; } = string.Empty;
        public DateOnly Date { get; init; }
        public DateOnly AccountingMonth { get; init; }
        public Guid SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;
        public Guid ExpenseTypeId { get; init; }
        public string ExpenseTypeName { get; init; } = string.Empty;
        public decimal AccrualAmount { get; init; }
        public decimal ExpenseAmount { get; init; }
        public decimal Difference { get; init; }
        public string? DocumentNumber { get; init; }
        public string? Comment { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public Guid RowId { get; init; }
    }
}
