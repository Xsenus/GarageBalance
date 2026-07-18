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
        var monthlyRows = dbContext.Database.IsNpgsql()
            ? await GetPostgresMonthlyRowsAsync(periodFrom, periodTo, sort, offset, limit, cancellationToken)
            : GetFallbackMonthlyRows(
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

    private async Task<IReadOnlyList<MonthlyReportQueryRow>> GetPostgresMonthlyRowsAsync(
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
            WITH months AS (
                SELECT generate_series(@period_from::date, @period_to::date, interval '1 month')::date AS month
            ), operation_totals AS (
                SELECT "AccountingMonth" AS month,
                       COALESCE(SUM("Amount") FILTER (WHERE "OperationKind" = 'income'), 0) AS income_total,
                       COALESCE(SUM("Amount") FILTER (WHERE "OperationKind" = 'expense'), 0) AS expense_total,
                       COUNT(*) FILTER (WHERE "OperationKind" IN ('income', 'expense'))::int AS operation_count
                FROM financial_operations
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "AccountingMonth"
            ), accrual_totals AS (
                SELECT "AccountingMonth" AS month,
                       COALESCE(SUM("Amount"), 0) AS accrual_total,
                       COUNT(*)::int AS accrual_count
                FROM accruals
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "AccountingMonth"
            ), reading_totals AS (
                SELECT "AccountingMonth" AS month, COUNT(*)::int AS reading_count
                FROM meter_readings
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "AccountingMonth"
            ), starting_balance AS (
                SELECT COALESCE(SUM("StartingBalance"), 0) AS amount,
                       COUNT(*) FILTER (WHERE "StartingBalance" <> 0)::int AS row_count
                FROM garages
                WHERE "IsArchived" = FALSE
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
                       COALESCE(operation_totals.operation_count, 0)::int AS "OperationCount",
                       (COALESCE(accrual_totals.accrual_count, 0)
                           + CASE WHEN months.month = @period_from::date AND starting_balance.amount <> 0 THEN 1 ELSE 0 END)::int AS "AccrualCount",
                       COALESCE(reading_totals.reading_count, 0)::int AS "MeterReadingCount"
                FROM months
                CROSS JOIN starting_balance
                LEFT JOIN operation_totals ON operation_totals.month = months.month
                LEFT JOIN accrual_totals ON accrual_totals.month = months.month
                LEFT JOIN reading_totals ON reading_totals.month = months.month
            )
            SELECT *
            FROM report_rows
            ORDER BY {{sortColumn}} {{direction}}, "AccountingMonth" DESC
            OFFSET @offset
            {{limitClause}}
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

        return await dbContext.Database
            .SqlQueryRaw<MonthlyReportQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
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
}
