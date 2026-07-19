using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfConsolidatedMonthlyReportQuery(GarageBalanceDbContext dbContext) : IConsolidatedMonthlyReportQuery
{
    private const int OperationMonthlyCategory = 1;
    private const int AccrualMonthlyCategory = 2;
    private const int MeterReadingMonthlyCategory = 3;
    private const int StartingBalanceCategory = 4;
    private const int IncomeBreakdownCategory = 5;
    private const int ExpenseBreakdownCategory = 6;
    private const int MonthlyPageCategory = 7;

    public async Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        CancellationToken cancellationToken) =>
        await GetMonthlyDataAsync(periodFrom, periodTo, new ReportSort("accountingMonth", false), 0, null, cancellationToken);

    public async Task<ConsolidatedMonthlyReportData> GetMonthlyDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresDataAsync(periodFrom, periodTo, sort, offset, limit, cancellationToken);
        }

        var operations = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.AccountingMonth >= periodFrom && operation.AccountingMonth <= periodTo);
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual => !accrual.IsCanceled && accrual.AccountingMonth >= periodFrom && accrual.AccountingMonth <= periodTo);
        var meterReadings = dbContext.MeterReadings.AsNoTracking()
            .Where(reading => !reading.IsCanceled && reading.AccountingMonth >= periodFrom && reading.AccountingMonth <= periodTo);

        var operationMonthlyQuery = operations
            .Where(operation =>
                operation.OperationKind == FinancialOperationKinds.Income ||
                operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => new { operation.AccountingMonth, operation.OperationKind })
            .Select(group => new
            {
                Category = OperationMonthlyCategory,
                Month = (DateOnly?)group.Key.AccountingMonth,
                Kind = (string?)group.Key.OperationKind,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            });
        var accrualMonthlyQuery = accruals
            .GroupBy(accrual => accrual.AccountingMonth)
            .Select(group => new
            {
                Category = AccrualMonthlyCategory,
                Month = (DateOnly?)group.Key,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(item => item.Amount),
                Count = group.Count()
            });
        var readingMonthlyQuery = meterReadings
            .GroupBy(reading => reading.AccountingMonth)
            .Select(group => new
            {
                Category = MeterReadingMonthlyCategory,
                Month = (DateOnly?)group.Key,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = 0m,
                Count = group.Count()
            });
        var garageStartingBalanceQuery = dbContext.Garages.AsNoTracking()
            .Where(garage => !garage.IsArchived && garage.StartingBalance != 0)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Category = StartingBalanceCategory,
                Month = (DateOnly?)periodFrom,
                Kind = (string?)null,
                TypeId = (Guid?)null,
                Name = (string?)null,
                Amount = group.Sum(garage => garage.StartingBalance),
                Count = group.Count()
            });
        var incomeBreakdownQuery = operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Income)
            .GroupBy(operation => new
            {
                operation.IncomeTypeId,
                Name = operation.IncomeType == null ? "Без вида поступления" : operation.IncomeType.Name
            })
            .Select(group => new
            {
                Category = IncomeBreakdownCategory,
                Month = (DateOnly?)null,
                Kind = (string?)null,
                TypeId = group.Key.IncomeTypeId,
                Name = (string?)group.Key.Name,
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });
        var expenseBreakdownQuery = operations
            .Where(operation => operation.OperationKind == FinancialOperationKinds.Expense)
            .GroupBy(operation => new
            {
                operation.ExpenseTypeId,
                Name = operation.ExpenseType == null ? "Без вида выплаты" : operation.ExpenseType.Name
            })
            .Select(group => new
            {
                Category = ExpenseBreakdownCategory,
                Month = (DateOnly?)null,
                Kind = (string?)null,
                TypeId = group.Key.ExpenseTypeId,
                Name = (string?)group.Key.Name,
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });

        var rows = await operationMonthlyQuery
            .Concat(accrualMonthlyQuery)
            .Concat(readingMonthlyQuery)
            .Concat(garageStartingBalanceQuery)
            .Concat(incomeBreakdownQuery)
            .Concat(expenseBreakdownQuery)
            .ToListAsync(cancellationToken);
        var incomeByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Income)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var expenseByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Expense)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var incomeBreakdown = rows
            .Where(row => row.Category == IncomeBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var expenseBreakdown = rows
            .Where(row => row.Category == ExpenseBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var accrualByMonth = rows
            .Where(row => row.Category == AccrualMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var readingsByMonth = rows
            .Where(row => row.Category == MeterReadingMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new CountByMonth(row.Month!.Value, row.Count))
            .ToList();
        var garageStartingBalanceTotal = rows
            .Where(row => row.Category == StartingBalanceCategory)
            .Sum(row => row.Amount);
        var monthlyRows = GetFallbackMonthlyRows(
            periodFrom,
            periodTo,
            incomeByMonth,
            expenseByMonth,
            accrualByMonth,
            readingsByMonth,
            garageStartingBalanceTotal,
            sort,
            offset,
            limit);

        return new ConsolidatedMonthlyReportData(
            incomeByMonth,
            expenseByMonth,
            accrualByMonth,
            readingsByMonth,
            garageStartingBalanceTotal,
            incomeBreakdown,
            expenseBreakdown,
            monthlyRows,
            MonthPeriod.Enumerate(periodFrom, periodTo).Count());
    }

    private async Task<ConsolidatedMonthlyReportData> GetPostgresDataAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        ReportSort sort,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        var sortColumn = sort.Field switch
        {
            "incomeTotal" => "\"IncomeTotal\"",
            "expenseTotal" => "\"ExpenseTotal\"",
            "accrualTotal" => "\"AccrualTotal\"",
            "balance" => "\"Balance\"",
            "debt" => "\"Debt\"",
            "operationCount" => "\"OperationCount\"",
            "accrualCount" => "\"AccrualCount\"",
            "meterReadingCount" => "\"MeterReadingCount\"",
            _ => "\"AccountingMonth\""
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH operations AS MATERIALIZED (
                SELECT "AccountingMonth", "OperationKind", "IncomeTypeId", "ExpenseTypeId", "Amount"
                FROM financial_operations
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                  AND "OperationKind" IN ('income', 'expense')
            ), accrual_source AS MATERIALIZED (
                SELECT "AccountingMonth", "Amount"
                FROM accruals
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
            ), reading_source AS MATERIALIZED (
                SELECT "AccountingMonth"
                FROM meter_readings
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
            ), garage_source AS MATERIALIZED (
                SELECT "StartingBalance"
                FROM garages
                WHERE "IsArchived" = FALSE
            ), months AS (
                SELECT generate_series(@period_from::date, @period_to::date, interval '1 month')::date AS month
            ), operation_totals AS (
                SELECT "AccountingMonth" AS month,
                       COALESCE(SUM("Amount") FILTER (WHERE "OperationKind" = 'income'), 0) AS income_total,
                       COALESCE(SUM("Amount") FILTER (WHERE "OperationKind" = 'expense'), 0) AS expense_total,
                       COUNT(*) FILTER (WHERE "OperationKind" = 'income')::int AS income_count,
                       COUNT(*) FILTER (WHERE "OperationKind" = 'expense')::int AS expense_count
                FROM operations
                GROUP BY "AccountingMonth"
            ), accrual_totals AS (
                SELECT "AccountingMonth" AS month,
                       COALESCE(SUM("Amount"), 0) AS accrual_total,
                       COUNT(*)::int AS accrual_count
                FROM accrual_source
                GROUP BY "AccountingMonth"
            ), reading_totals AS (
                SELECT "AccountingMonth" AS month, COUNT(*)::int AS reading_count
                FROM reading_source
                GROUP BY "AccountingMonth"
            ), starting_balance AS (
                SELECT COALESCE(SUM("StartingBalance"), 0) AS amount
                FROM garage_source
            ), income_breakdown AS (
                SELECT operation."IncomeTypeId" AS type_id,
                       COALESCE(income_type."Name", 'Без вида поступления') AS name,
                       SUM(operation."Amount") AS amount
                FROM operations operation
                LEFT JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
                WHERE operation."OperationKind" = 'income'
                GROUP BY operation."IncomeTypeId", income_type."Name"
            ), expense_breakdown AS (
                SELECT operation."ExpenseTypeId" AS type_id,
                       COALESCE(expense_type."Name", 'Без вида выплаты') AS name,
                       SUM(operation."Amount") AS amount
                FROM operations operation
                LEFT JOIN expense_types expense_type ON expense_type."Id" = operation."ExpenseTypeId"
                WHERE operation."OperationKind" = 'expense'
                GROUP BY operation."ExpenseTypeId", expense_type."Name"
            ), report_rows AS (
                SELECT months.month AS "AccountingMonth",
                       COALESCE(operation_totals.income_total, 0) AS "IncomeTotal",
                       COALESCE(operation_totals.expense_total, 0) AS "ExpenseTotal",
                       COALESCE(accrual_totals.accrual_total, 0)
                           + CASE WHEN months.month = @period_from::date THEN starting_balance.amount ELSE 0 END AS "AccrualTotal",
                       COALESCE(operation_totals.income_total, 0) - COALESCE(operation_totals.expense_total, 0) AS "Balance",
                       COALESCE(accrual_totals.accrual_total, 0)
                           + CASE WHEN months.month = @period_from::date THEN starting_balance.amount ELSE 0 END
                           - COALESCE(operation_totals.income_total, 0) AS "Debt",
                       (COALESCE(operation_totals.income_count, 0) + COALESCE(operation_totals.expense_count, 0))::int AS "OperationCount",
                       (COALESCE(accrual_totals.accrual_count, 0)
                           + CASE WHEN months.month = @period_from::date AND starting_balance.amount <> 0 THEN 1 ELSE 0 END)::int AS "AccrualCount",
                       COALESCE(reading_totals.reading_count, 0)::int AS "MeterReadingCount"
                FROM months
                CROSS JOIN starting_balance
                LEFT JOIN operation_totals ON operation_totals.month = months.month
                LEFT JOIN accrual_totals ON accrual_totals.month = months.month
                LEFT JOIN reading_totals ON reading_totals.month = months.month
            ), monthly_page AS (
                SELECT report_rows.*,
                       ROW_NUMBER() OVER (ORDER BY {{sortColumn}} {{direction}}, "AccountingMonth" DESC)::int AS row_order
                FROM report_rows
                ORDER BY {{sortColumn}} {{direction}}, "AccountingMonth" DESC
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{OperationMonthlyCategory}} AS "Category", 0 AS "RowOrder", month AS "Month", 'income'::text AS "Kind",
                   NULL::uuid AS "TypeId", NULL::text AS "Name", income_total AS "Amount", income_count AS "Count",
                   NULL::date AS "AccountingMonth", 0::numeric AS "IncomeTotal", 0::numeric AS "ExpenseTotal",
                   0::numeric AS "AccrualTotal", 0::numeric AS "Balance", 0::numeric AS "Debt",
                   0 AS "OperationCount", 0 AS "AccrualCount", 0 AS "MeterReadingCount"
            FROM operation_totals WHERE income_count > 0
            UNION ALL
            SELECT {{OperationMonthlyCategory}}, 0, month, 'expense'::text, NULL::uuid, NULL::text,
                   expense_total, expense_count, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM operation_totals WHERE expense_count > 0
            UNION ALL
            SELECT {{AccrualMonthlyCategory}}, 0, month, NULL::text, NULL::uuid, NULL::text,
                   accrual_total, accrual_count, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM accrual_totals
            UNION ALL
            SELECT {{MeterReadingMonthlyCategory}}, 0, month, NULL::text, NULL::uuid, NULL::text,
                   0::numeric, reading_count, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM reading_totals
            UNION ALL
            SELECT {{StartingBalanceCategory}}, 0, @period_from::date, NULL::text, NULL::uuid, NULL::text,
                   amount, 0, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM starting_balance
            UNION ALL
            SELECT {{IncomeBreakdownCategory}}, 0, NULL::date, NULL::text, type_id, name,
                   amount, 0, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM income_breakdown
            UNION ALL
            SELECT {{ExpenseBreakdownCategory}}, 0, NULL::date, NULL::text, type_id, name,
                   amount, 0, NULL::date, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0::numeric, 0, 0, 0
            FROM expense_breakdown
            UNION ALL
            SELECT {{MonthlyPageCategory}}, row_order, NULL::date, NULL::text, NULL::uuid, NULL::text,
                   0::numeric, 0, "AccountingMonth", "IncomeTotal", "ExpenseTotal", "AccrualTotal", "Balance", "Debt",
                   "OperationCount", "AccrualCount", "MeterReadingCount"
            FROM monthly_page
            ORDER BY "Category", "RowOrder", "Month", "Name"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateOnly>("period_from", periodFrom),
            new NpgsqlParameter<DateOnly>("period_to", periodTo),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<ConsolidatedReportCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var incomeByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Income)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var expenseByMonth = rows
            .Where(row => row.Category == OperationMonthlyCategory && row.Kind == FinancialOperationKinds.Expense)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var accrualByMonth = rows
            .Where(row => row.Category == AccrualMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new AmountCountByMonth(row.Month!.Value, row.Amount, row.Count))
            .ToList();
        var readingsByMonth = rows
            .Where(row => row.Category == MeterReadingMonthlyCategory)
            .OrderBy(row => row.Month)
            .Select(row => new CountByMonth(row.Month!.Value, row.Count))
            .ToList();
        var startingBalance = rows.Single(row => row.Category == StartingBalanceCategory).Amount;
        var incomeBreakdown = rows
            .Where(row => row.Category == IncomeBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var expenseBreakdown = rows
            .Where(row => row.Category == ExpenseBreakdownCategory)
            .OrderBy(row => row.Name)
            .Select(row => new NamedAmountTotal(row.TypeId, row.Name!, row.Amount))
            .ToList();
        var monthlyRows = rows
            .Where(row => row.Category == MonthlyPageCategory)
            .Select(row => new MonthlyReportQueryRow(
                row.AccountingMonth!.Value,
                row.IncomeTotal,
                row.ExpenseTotal,
                row.AccrualTotal,
                row.Balance,
                row.Debt,
                row.OperationCount,
                row.AccrualCount,
                row.MeterReadingCount))
            .ToList();

        return new ConsolidatedMonthlyReportData(
            incomeByMonth,
            expenseByMonth,
            accrualByMonth,
            readingsByMonth,
            startingBalance,
            incomeBreakdown,
            expenseBreakdown,
            monthlyRows,
            MonthPeriod.Enumerate(periodFrom, periodTo).Count());
    }

    private static IReadOnlyList<MonthlyReportQueryRow> GetFallbackMonthlyRows(
        DateOnly periodFrom,
        DateOnly periodTo,
        IReadOnlyList<AmountCountByMonth> incomeRows,
        IReadOnlyList<AmountCountByMonth> expenseRows,
        IReadOnlyList<AmountCountByMonth> accrualRows,
        IReadOnlyList<CountByMonth> readingRows,
        decimal startingBalance,
        ReportSort sort,
        int offset,
        int? limit)
    {
        var incomeByMonth = incomeRows.ToDictionary(row => row.Month);
        var expenseByMonth = expenseRows.ToDictionary(row => row.Month);
        var accrualByMonth = accrualRows.ToDictionary(row => row.Month);
        var readingsByMonth = readingRows.ToDictionary(row => row.Month);
        var rows = MonthPeriod.Enumerate(periodFrom, periodTo).Select(month =>
        {
            incomeByMonth.TryGetValue(month, out var income);
            expenseByMonth.TryGetValue(month, out var expense);
            accrualByMonth.TryGetValue(month, out var accrual);
            readingsByMonth.TryGetValue(month, out var readings);
            var opening = month == periodFrom ? startingBalance : 0m;
            return new MonthlyReportQueryRow(
                month,
                income.Amount,
                expense.Amount,
                accrual.Amount + opening,
                income.Amount - expense.Amount,
                accrual.Amount + opening - income.Amount,
                income.Count + expense.Count,
                accrual.Count + (opening != 0 ? 1 : 0),
                readings.Count);
        });
        var ordered = ApplySort(rows, sort).ThenByDescending(row => row.AccountingMonth);
        var page = offset > 0 ? ordered.Skip(offset) : ordered;
        return (limit is > 0 ? page.Take(limit.Value) : page).ToList();
    }

    private static IOrderedEnumerable<MonthlyReportQueryRow> ApplySort(IEnumerable<MonthlyReportQueryRow> rows, ReportSort sort) =>
        sort.Field switch
        {
            "incomeTotal" => sort.Descending ? rows.OrderByDescending(row => row.IncomeTotal) : rows.OrderBy(row => row.IncomeTotal),
            "expenseTotal" => sort.Descending ? rows.OrderByDescending(row => row.ExpenseTotal) : rows.OrderBy(row => row.ExpenseTotal),
            "accrualTotal" => sort.Descending ? rows.OrderByDescending(row => row.AccrualTotal) : rows.OrderBy(row => row.AccrualTotal),
            "balance" => sort.Descending ? rows.OrderByDescending(row => row.Balance) : rows.OrderBy(row => row.Balance),
            "debt" => sort.Descending ? rows.OrderByDescending(row => row.Debt) : rows.OrderBy(row => row.Debt),
            "operationCount" => sort.Descending ? rows.OrderByDescending(row => row.OperationCount) : rows.OrderBy(row => row.OperationCount),
            "accrualCount" => sort.Descending ? rows.OrderByDescending(row => row.AccrualCount) : rows.OrderBy(row => row.AccrualCount),
            "meterReadingCount" => sort.Descending ? rows.OrderByDescending(row => row.MeterReadingCount) : rows.OrderBy(row => row.MeterReadingCount),
            _ => sort.Descending ? rows.OrderByDescending(row => row.AccountingMonth) : rows.OrderBy(row => row.AccountingMonth)
        };

    private sealed record ConsolidatedReportCombinedQueryRow(
        int Category,
        int RowOrder,
        DateOnly? Month,
        string? Kind,
        Guid? TypeId,
        string? Name,
        decimal Amount,
        int Count,
        DateOnly? AccountingMonth,
        decimal IncomeTotal,
        decimal ExpenseTotal,
        decimal AccrualTotal,
        decimal Balance,
        decimal Debt,
        int OperationCount,
        int AccrualCount,
        int MeterReadingCount);
}
