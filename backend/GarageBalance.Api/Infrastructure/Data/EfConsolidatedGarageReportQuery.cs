using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfConsolidatedGarageReportQuery(GarageBalanceDbContext dbContext) : IConsolidatedGarageReportQuery
{
    public Task<ConsolidatedGarageRowsData> GetGarageRowsAsync(
        string? search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (IsNpgsql())
        {
            return GetPostgresRowsAsync(search, periodFrom, periodTo, limit, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return GetRowsWithoutSearchAsync(periodFrom, periodTo, limit, cancellationToken);
        }

        return GetRowsWithClientSearchAsync(search, periodFrom, periodTo, limit, cancellationToken);
    }

    private async Task<ConsolidatedGarageRowsData> GetPostgresRowsAsync(
        string? search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int CountCategory = 2;
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (
                  LOWER(garage."Number") LIKE '%' || @search || '%'
                  OR LOWER(owner."LastName") LIKE '%' || @search || '%'
                  OR LOWER(owner."FirstName") LIKE '%' || @search || '%'
                  OR LOWER(COALESCE(owner."MiddleName", '')) LIKE '%' || @search || '%'
                  OR LOWER(owner."LastName" || ' ' || owner."FirstName") LIKE '%' || @search || '%'
                  OR LOWER(owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '')) LIKE '%' || @search || '%'
              )
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH income_totals AS (
                SELECT "GarageId" AS garage_id, SUM("Amount") AS amount
                FROM financial_operations
                WHERE "IsCanceled" = FALSE
                  AND "OperationKind" = 'income'
                  AND "GarageId" IS NOT NULL
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "GarageId"
            ), accrual_totals AS (
                SELECT "GarageId" AS garage_id, SUM("Amount") AS amount
                FROM accruals
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "GarageId"
            ), reading_totals AS (
                SELECT "GarageId" AS garage_id, COUNT(*)::int AS row_count
                FROM meter_readings
                WHERE "IsCanceled" = FALSE
                  AND "AccountingMonth" >= @period_from::date
                  AND "AccountingMonth" <= @period_to::date
                GROUP BY "GarageId"
            ), filtered_rows AS (
                SELECT garage."Id" AS garage_id,
                       garage."Number" AS garage_number,
                       owner."LastName" AS owner_last_name,
                       owner."FirstName" AS owner_first_name,
                       owner."MiddleName" AS owner_middle_name,
                       COALESCE(income_totals.amount, 0) AS income_total,
                       garage."StartingBalance" + COALESCE(accrual_totals.amount, 0) AS accrual_total,
                       COALESCE(reading_totals.row_count, 0)::int AS meter_reading_count
                FROM garages garage
                LEFT JOIN owners owner ON owner."Id" = garage."OwnerId"
                LEFT JOIN income_totals ON income_totals.garage_id = garage."Id"
                LEFT JOIN accrual_totals ON accrual_totals.garage_id = garage."Id"
                LEFT JOIN reading_totals ON reading_totals.garage_id = garage."Id"
                WHERE garage."IsArchived" = FALSE
                  {{searchClause}}
                  AND (COALESCE(income_totals.amount, 0) <> 0
                       OR garage."StartingBalance" + COALESCE(accrual_totals.amount, 0) <> 0
                       OR COALESCE(reading_totals.row_count, 0) <> 0)
            ), page_rows AS (
                SELECT filtered_rows.*,
                       ROW_NUMBER() OVER (ORDER BY garage_number, garage_id)::int AS row_order
                FROM filtered_rows
                ORDER BY garage_number, garage_id
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", garage_id AS "GarageId",
                   garage_number AS "GarageNumber", owner_last_name AS "OwnerLastName",
                   owner_first_name AS "OwnerFirstName", owner_middle_name AS "OwnerMiddleName",
                   income_total AS "IncomeTotal", accrual_total AS "AccrualTotal",
                   meter_reading_count AS "MeterReadingCount", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{CountCategory}}, 0, NULL::uuid, NULL::text, NULL::text, NULL::text, NULL::text,
                   0::numeric, 0::numeric, 0, COUNT(*)::int
            FROM filtered_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateOnly>("period_from", periodFrom),
            new NpgsqlParameter<DateOnly>("period_to", periodTo)
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new NpgsqlParameter<string>("search", search.Trim().ToLowerInvariant()));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var queryRows = await dbContext.Database
            .SqlQueryRaw<ConsolidatedGarageCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var rowCount = queryRows.Single(row => row.Category == CountCategory).RowCount;
        var rows = queryRows
            .Where(row => row.Category == PageCategory)
            .Select(row => new ConsolidatedGarageRowData(
                row.GarageId!.Value,
                row.GarageNumber!,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTotal,
                row.AccrualTotal,
                row.MeterReadingCount))
            .ToList();
        return new ConsolidatedGarageRowsData(rowCount, rows);
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithoutSearchAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var garages = dbContext.Garages.AsNoTracking().Where(garage => !garage.IsArchived);
        return await ExecuteBoundedRowsAsync(
            BuildRowsQuery(garages, periodFrom, periodTo),
            limit,
            cancellationToken);
    }

    private IQueryable<ConsolidatedGarageProjectionRow> BuildRowsQuery(
        IQueryable<Garage> garages,
        DateOnly periodFrom,
        DateOnly periodTo) =>
        garages
            .Select(garage => new
            {
                GarageId = garage.Id,
                GarageNumber = garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                IncomeTotal = dbContext.FinancialOperations.AsNoTracking()
                    .Where(operation =>
                        !operation.IsCanceled &&
                        operation.OperationKind == FinancialOperationKinds.Income &&
                        operation.GarageId == garage.Id &&
                        operation.AccountingMonth >= periodFrom &&
                        operation.AccountingMonth <= periodTo)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m,
                AccrualTotal = garage.StartingBalance + (dbContext.Accruals.AsNoTracking()
                    .Where(accrual =>
                        !accrual.IsCanceled &&
                        accrual.GarageId == garage.Id &&
                        accrual.AccountingMonth >= periodFrom &&
                        accrual.AccountingMonth <= periodTo)
                    .Sum(accrual => (decimal?)accrual.Amount) ?? 0m),
                MeterReadingCount = dbContext.MeterReadings.AsNoTracking()
                    .Count(reading =>
                        !reading.IsCanceled &&
                        reading.GarageId == garage.Id &&
                        reading.AccountingMonth >= periodFrom &&
                        reading.AccountingMonth <= periodTo)
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .OrderBy(row => row.GarageNumber)
            .Select(row => new ConsolidatedGarageProjectionRow(
                row.GarageId,
                row.GarageNumber,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTotal,
                row.AccrualTotal,
                row.MeterReadingCount));

    private static async Task<ConsolidatedGarageRowsData> ExecuteBoundedRowsAsync(
        IQueryable<ConsolidatedGarageProjectionRow> query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var rowCount = await query.CountAsync(cancellationToken);
        var rows = await ApplyLimit(query, limit).ToListAsync(cancellationToken);
        return new ConsolidatedGarageRowsData(
            rowCount,
            rows.Select(row => row.ToData()).ToList());
    }

    private async Task<ConsolidatedGarageRowsData> GetRowsWithClientSearchAsync(
        string search,
        DateOnly periodFrom,
        DateOnly periodTo,
        int? limit,
        CancellationToken cancellationToken)
    {
        var normalized = search.Trim();
        var garageQuery = dbContext.Garages.AsNoTracking()
            .Include(garage => garage.Owner)
            .Where(garage => !garage.IsArchived);
        var garages = (await garageQuery.OrderBy(garage => garage.Number).ToListAsync(cancellationToken))
            .Where(garage =>
                garage.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                (garage.Owner?.FullName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        var garageIds = garages.Select(garage => garage.Id).ToList();

        var incomeByGarageQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                operation.AccountingMonth >= periodFrom &&
                operation.AccountingMonth <= periodTo)
            .GroupBy(operation => operation.GarageId!.Value)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "income",
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });
        var accrualByGarageQuery = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .GroupBy(accrual => accrual.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "accrual",
                Amount = group.Sum(item => item.Amount),
                Count = 0
            });
        var readingsByGarageQuery = dbContext.MeterReadings.AsNoTracking()
            .Where(reading =>
                !reading.IsCanceled &&
                garageIds.Contains(reading.GarageId) &&
                reading.AccountingMonth >= periodFrom &&
                reading.AccountingMonth <= periodTo)
            .GroupBy(reading => reading.GarageId)
            .Select(group => new
            {
                GarageId = group.Key,
                Kind = "reading",
                Amount = 0m,
                Count = group.Count()
            });
        var aggregateRows = await incomeByGarageQuery
            .Concat(accrualByGarageQuery)
            .Concat(readingsByGarageQuery)
            .ToListAsync(cancellationToken);
        var incomeLookup = aggregateRows
            .Where(row => row.Kind == "income")
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var accrualLookup = aggregateRows
            .Where(row => row.Kind == "accrual")
            .ToDictionary(row => row.GarageId, row => row.Amount);
        var readingsLookup = aggregateRows
            .Where(row => row.Kind == "reading")
            .ToDictionary(row => row.GarageId, row => row.Count);

        var rows = garages.Select(garage =>
            {
                incomeLookup.TryGetValue(garage.Id, out var income);
                accrualLookup.TryGetValue(garage.Id, out var accrual);
                readingsLookup.TryGetValue(garage.Id, out var readings);
                return new ConsolidatedGarageRowData(
                    garage.Id,
                    garage.Number,
                    garage.Owner?.LastName,
                    garage.Owner?.FirstName,
                    garage.Owner?.MiddleName,
                    income,
                    accrual + garage.StartingBalance,
                    readings);
            })
            .Where(row => row.IncomeTotal != 0 || row.AccrualTotal != 0 || row.MeterReadingCount != 0)
            .ToList();
        return new ConsolidatedGarageRowsData(rows.Count, ApplyLimit(rows, limit).ToList());
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> rows, int? limit) =>
        limit is > 0 ? rows.Take(limit.Value) : rows;

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;

    private sealed record ConsolidatedGarageProjectionRow(
        Guid GarageId,
        string GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        decimal IncomeTotal,
        decimal AccrualTotal,
        int MeterReadingCount)
    {
        public ConsolidatedGarageRowData ToData() =>
            new(GarageId, GarageNumber, OwnerLastName, OwnerFirstName, OwnerMiddleName, IncomeTotal, AccrualTotal, MeterReadingCount);
    }

    private sealed record ConsolidatedGarageCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? GarageId,
        string? GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        decimal IncomeTotal,
        decimal AccrualTotal,
        int MeterReadingCount,
        int RowCount);
}
