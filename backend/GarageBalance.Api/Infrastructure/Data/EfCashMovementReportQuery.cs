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
                       COALESCE(
                           operation."ExpensePaymentType" = 'with_receipt',
                           operation."DocumentNumber" IS NOT NULL AND operation."DocumentNumber" <> ''
                       ) AS has_receipt,
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
                   expense_type_name AS "ExpenseTypeName", has_receipt AS "HasReceipt", document_number AS "DocumentNumber", comment AS "Comment",
                   0::numeric AS "Total", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::date, 0::numeric, NULL::text, NULL::text, FALSE, NULL::text, NULL::text,
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
                row.HasReceipt,
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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresBankDepositsAsync(dateFrom, dateTo, search, offset, limit, sort, cancellationToken);
        }

        var transfers = await dbContext.CashBankTransfers.AsNoTracking()
            .Where(transfer => !transfer.IsCanceled && transfer.TransferDate >= dateFrom && transfer.TransferDate <= dateTo)
            .ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            transfers = transfers.Where(transfer =>
                    transfer.Comment?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        transfers = ApplyBankDepositSort(transfers, sort).ThenByDescending(transfer => transfer.Id).ToList();
        return new BankDepositReportData(
            ApplyPage(transfers, offset, limit).Select(ToBankDepositQueryRow).ToList(),
            transfers.Sum(transfer => transfer.Amount),
            transfers.Count);
    }

    private async Task<BankDepositReportData> GetPostgresBankDepositsAsync(
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
            "comment" => "comment",
            _ => "transfer_date"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND LOWER(COALESCE(transfer."Comment", '')) LIKE '%' || @search || '%'
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT transfer."Id" AS id,
                       transfer."TransferDate" AS transfer_date,
                       transfer."Amount" AS amount,
                       transfer."Comment" AS comment
                FROM cash_bank_transfers transfer
                WHERE transfer."IsCanceled" = FALSE
                  AND transfer."TransferDate" >= @date_from::date
                  AND transfer."TransferDate" <= @date_to::date
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
                   transfer_date AS "TransferDate", amount AS "Amount", comment AS "Comment",
                   0::numeric AS "Total", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::date, 0::numeric, NULL::text,
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
            .SqlQueryRaw<BankDepositCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = rows.Single(row => row.Category == TotalsCategory);
        var operations = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new BankDepositQueryRow(
                row.Id!.Value,
                row.TransferDate!.Value,
                row.Amount,
                row.Comment))
            .ToList();
        return new BankDepositReportData(operations, totals.Total, totals.RowCount);
    }

    private static IOrderedQueryable<FinancialOperation> ApplyCashPaymentSort(IQueryable<FinancialOperation> query, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? query.OrderByDescending(operation => operation.Amount) : query.OrderBy(operation => operation.Amount),
            "hasReceipt" => sort.Descending
                ? query.OrderByDescending(operation =>
                    operation.ExpensePaymentType == ExpensePaymentTypes.WithReceipt ||
                    (operation.ExpensePaymentType == null && operation.DocumentNumber != null && operation.DocumentNumber != string.Empty))
                : query.OrderBy(operation =>
                    operation.ExpensePaymentType == ExpensePaymentTypes.WithReceipt ||
                    (operation.ExpensePaymentType == null && operation.DocumentNumber != null && operation.DocumentNumber != string.Empty)),
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
            "hasReceipt" => sort.Descending ? operations.OrderByDescending(HasReceipt) : operations.OrderBy(HasReceipt),
            "purpose" => sort.Descending ? operations.OrderByDescending(BuildCashPaymentPurpose, StringComparer.Ordinal) : operations.OrderBy(BuildCashPaymentPurpose, StringComparer.Ordinal),
            "supplierName" => sort.Descending ? operations.OrderByDescending(operation => operation.Supplier?.Name, StringComparer.Ordinal) : operations.OrderBy(operation => operation.Supplier?.Name, StringComparer.Ordinal),
            "expenseTypeName" => sort.Descending ? operations.OrderByDescending(operation => operation.ExpenseType?.Name, StringComparer.Ordinal) : operations.OrderBy(operation => operation.ExpenseType?.Name, StringComparer.Ordinal),
            "documentNumber" => sort.Descending ? operations.OrderByDescending(operation => operation.DocumentNumber, StringComparer.Ordinal) : operations.OrderBy(operation => operation.DocumentNumber, StringComparer.Ordinal),
            _ => sort.Descending ? operations.OrderByDescending(operation => operation.OperationDate) : operations.OrderBy(operation => operation.OperationDate)
        };

    private static IOrderedEnumerable<CashBankTransfer> ApplyBankDepositSort(IEnumerable<CashBankTransfer> transfers, ReportSort sort) =>
        sort.Field switch
        {
            "amount" => sort.Descending ? transfers.OrderByDescending(transfer => transfer.Amount) : transfers.OrderBy(transfer => transfer.Amount),
            "comment" => sort.Descending ? transfers.OrderByDescending(transfer => transfer.Comment, StringComparer.Ordinal) : transfers.OrderBy(transfer => transfer.Comment, StringComparer.Ordinal),
            _ => sort.Descending ? transfers.OrderByDescending(transfer => transfer.TransferDate) : transfers.OrderBy(transfer => transfer.TransferDate)
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
            HasReceipt(operation),
            operation.DocumentNumber,
            operation.Comment);

    private static bool HasReceipt(FinancialOperation operation) =>
        operation.ExpensePaymentType == ExpensePaymentTypes.WithReceipt ||
        (operation.ExpensePaymentType is null && !string.IsNullOrWhiteSpace(operation.DocumentNumber));

    private static BankDepositQueryRow ToBankDepositQueryRow(CashBankTransfer transfer) =>
        new(transfer.Id, transfer.TransferDate, transfer.Amount, transfer.Comment);

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
        bool HasReceipt,
        string? DocumentNumber,
        string? Comment,
        decimal Total,
        int RowCount);

    private sealed record BankDepositCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        DateOnly? TransferDate,
        decimal Amount,
        string? Comment,
        decimal Total,
        int RowCount);
}
