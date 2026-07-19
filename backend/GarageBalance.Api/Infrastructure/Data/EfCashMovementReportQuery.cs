using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresCashPaymentsAsync(dateFrom, dateTo, search, offset, limit, sort, cancellationToken);
        }

        var query = dbContext.FinancialOperations.AsNoTracking()
            .Include(operation => operation.Supplier)
            .Include(operation => operation.ExpenseType)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Expense &&
                operation.OperationDate >= dateFrom &&
                operation.OperationDate <= dateTo);

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
            ApplyPage(orderedFallbackOperations, offset, limit)
                .Select(ToCashPaymentQueryRow)
                .ToList(),
            fallbackOperations.Sum(operation => operation.Amount),
            fallbackOperations.Count);
    }

    private async Task<CashPaymentReportData> GetPostgresCashPaymentsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var sortColumn = sort.Field switch
        {
            "amount" => "amount",
            "hasReceipt" => "has_receipt",
            "purpose" => "purpose",
            "supplierName" => "supplier_name",
            "expenseTypeName" => "expense_type_name",
            "documentNumber" => "document_number",
            _ => "operation_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (LOWER(supplier."Name") LIKE '%' || @search || '%'
                   OR LOWER(expense_type."Name") LIKE '%' || @search || '%'
                   OR LOWER(operation."DocumentNumber") LIKE '%' || @search || '%'
                   OR LOWER(operation."Comment") LIKE '%' || @search || '%')
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT operation."Id" AS id,
                       operation."OperationDate" AS operation_date,
                       operation."Amount" AS amount,
                       supplier."Name" AS supplier_name,
                       expense_type."Name" AS expense_type_name,
                       operation."DocumentNumber" AS document_number,
                       operation."Comment" AS comment,
                       operation."DocumentNumber" IS NOT NULL AND operation."DocumentNumber" <> '' AS has_receipt,
                       COALESCE(expense_type."Name", '') || ': ' || COALESCE(supplier."Name", '') AS purpose
                FROM financial_operations operation
                LEFT JOIN suppliers supplier ON supplier."Id" = operation."SupplierId"
                LEFT JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'expense'
                  AND operation."OperationDate" >= @date_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{searchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (ORDER BY {{sortColumn}} {{direction}}, id DESC)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, id DESC
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id",
                   operation_date AS "OperationDate", amount AS "Amount", supplier_name AS "SupplierName",
                   expense_type_name AS "ExpenseTypeName", document_number AS "DocumentNumber", comment AS "Comment",
                   0::numeric AS "Total", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::date, 0::numeric, NULL::text, NULL::text, NULL::text, NULL::text,
                   COALESCE(SUM(amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateOnly>("date_from", dateFrom),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter<string>("search", search.Trim().ToLowerInvariant()));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<CashPaymentCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = rows.Single(row => row.Category == TotalsCategory);
        var operations = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new CashPaymentQueryRow(
                row.Id!.Value,
                row.OperationDate!.Value,
                row.Amount,
                row.SupplierName,
                row.ExpenseTypeName,
                row.DocumentNumber,
                row.Comment))
            .ToList();
        return new CashPaymentReportData(operations, totals.Total, totals.RowCount);
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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresBankDepositsAsync(fromUtc, toExclusiveUtc, search, offset, limit, sort, cancellationToken);
        }

        var query = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FundOperationKinds.Deposit &&
                operation.IsCashToBankTransfer &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        var operations = IsSqliteProvider()
            ? (await dbContext.FundOperations.AsNoTracking()
                    .Include(operation => operation.Fund)
                    .ToListAsync(cancellationToken))
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.OperationKind == FundOperationKinds.Deposit &&
                    operation.IsCashToBankTransfer &&
                    operation.CreatedAtUtc >= fromUtc &&
                    operation.CreatedAtUtc < toExclusiveUtc)
                .ToList()
            : await query.ToListAsync(cancellationToken);
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
            ApplyPage(operations, offset, limit).Select(ToBankDepositQueryRow).ToList(),
            operations.Sum(operation => operation.Amount),
            operations.Count);
    }

    private async Task<BankDepositReportData> GetPostgresBankDepositsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toExclusiveUtc,
        string? search,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var sortColumn = sort.Field switch
        {
            "amount" => "amount",
            "fundName" => "fund_name",
            "comment" => "reason",
            _ => "created_at_utc"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (LOWER(fund."Name") LIKE '%' || @search || '%'
                   OR LOWER(operation."Reason") LIKE '%' || @search || '%')
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT operation."Id" AS id,
                       operation."CreatedAtUtc" AS created_at_utc,
                       operation."Amount" AS amount,
                       fund."Name" AS fund_name,
                       operation."Reason" AS reason
                FROM fund_operations operation
                INNER JOIN funds fund ON fund."Id" = operation."FundId"
                WHERE operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'deposit'
                  AND operation."IsCashToBankTransfer" = TRUE
                  AND operation."CreatedAtUtc" >= @from_utc
                  AND operation."CreatedAtUtc" < @to_exclusive_utc
                  {{searchClause}}
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (ORDER BY {{sortColumn}} {{direction}}, id DESC)::int AS row_order
                FROM filtered_rows
                ORDER BY {{sortColumn}} {{direction}}, id DESC
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id",
                   created_at_utc AS "CreatedAtUtc", amount AS "Amount", fund_name AS "FundName", reason AS "Reason",
                   0::numeric AS "Total", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::timestamptz, 0::numeric, ''::text, ''::text,
                   COALESCE(SUM(amount), 0), COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateTimeOffset>("from_utc", fromUtc),
            new NpgsqlParameter<DateTimeOffset>("to_exclusive_utc", toExclusiveUtc),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter<string>("search", search.Trim().ToLowerInvariant()));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<BankDepositCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = rows.Single(row => row.Category == TotalsCategory);
        var operations = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new BankDepositQueryRow(
                row.Id!.Value,
                row.CreatedAtUtc!.Value,
                row.Amount,
                row.FundName,
                row.Reason))
            .ToList();
        return new BankDepositReportData(operations, totals.Total, totals.RowCount);
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

    private static CashPaymentQueryRow ToCashPaymentQueryRow(FinancialOperation operation) =>
        new(
            operation.Id,
            operation.OperationDate,
            operation.Amount,
            operation.Supplier?.Name,
            operation.ExpenseType?.Name,
            operation.DocumentNumber,
            operation.Comment);

    private static BankDepositQueryRow ToBankDepositQueryRow(FundOperation operation) =>
        new(operation.Id, operation.CreatedAtUtc, operation.Amount, operation.Fund.Name, operation.Reason);

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

    private sealed record CashPaymentCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        DateOnly? OperationDate,
        decimal Amount,
        string? SupplierName,
        string? ExpenseTypeName,
        string? DocumentNumber,
        string? Comment,
        decimal Total,
        int RowCount);

    private sealed record BankDepositCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        DateTimeOffset? CreatedAtUtc,
        decimal Amount,
        string FundName,
        string Reason,
        decimal Total,
        int RowCount);
}
