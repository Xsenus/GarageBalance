using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageReportQuery(GarageBalanceDbContext dbContext) : IGarageReportQuery
{
    private const string StartingBalanceName = "Стартовый баланс";
    private const string GroupedIncomeTypeName = "ИТОГО";

    public Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        bool groupAccruals,
        int offset,
        int? limit,
        CancellationToken cancellationToken) =>
        GetRowsAsync(periodFrom, periodTo, search, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), groupAccruals, offset, limit, new ReportSort("accountingMonth", true), cancellationToken);

    public Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        bool groupAccruals,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken) =>
        GetRowsAsync(periodFrom, periodTo, search, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), groupAccruals, offset, limit, sort, cancellationToken);

    public async Task<GarageReportQueryData> GetRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        string? search,
        IReadOnlySet<Guid> selectedGarageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        bool groupAccruals,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        var dateTo = periodTo.AddMonths(1).AddDays(-1);
        if (IsNpgsql())
        {
            return await GetPostgresRowsAsync(
                periodFrom,
                periodTo,
                dateTo,
                search,
                selectedGarageIds,
                ownerIds,
                incomeTypeIds,
                groupAccruals,
                offset,
                limit,
                sort,
                cancellationToken);
        }

        var garages = dbContext.Garages.AsNoTracking().Where(garage => !garage.IsArchived);
        if (selectedGarageIds.Count > 0)
        {
            garages = garages.Where(garage => selectedGarageIds.Contains(garage.Id));
        }

        if (ownerIds.Count > 0)
        {
            garages = garages.Where(garage => garage.OwnerId != null && ownerIds.Contains(garage.OwnerId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            if (IsNpgsql())
            {
                var normalizedServerSearch = normalizedSearch.ToLowerInvariant();
                garages = garages.Where(garage =>
                    garage.Number.ToLower().Contains(normalizedServerSearch) ||
                    (garage.Owner != null && (
                        garage.Owner.LastName.ToLower().Contains(normalizedServerSearch) ||
                        garage.Owner.FirstName.ToLower().Contains(normalizedServerSearch) ||
                        (garage.Owner.MiddleName != null && garage.Owner.MiddleName.ToLower().Contains(normalizedServerSearch)) ||
                        (garage.Owner.LastName + " " + garage.Owner.FirstName).ToLower().Contains(normalizedServerSearch) ||
                        (garage.Owner.LastName + " " + garage.Owner.FirstName + " " + (garage.Owner.MiddleName ?? string.Empty)).ToLower().Contains(normalizedServerSearch))));
            }
            else
            {
                var matchingGarageIds = (await garages.Include(garage => garage.Owner).ToListAsync(cancellationToken))
                    .Where(garage =>
                        garage.Number.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (garage.Owner?.FullName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(garage => garage.Id)
                    .ToList();
                garages = garages.Where(garage => matchingGarageIds.Contains(garage.Id));
            }
        }

        var garageIds = garages.Select(garage => garage.Id);
        var startingBalances = garages
            .Where(garage => incomeTypeIds.Count == 0 && garage.StartingBalance != 0)
            .Select(garage => new
            {
                AccountingMonth = periodFrom,
                GarageId = garage.Id,
                GarageNumber = garage.Number,
                OwnerLastName = garage.Owner == null ? null : garage.Owner.LastName,
                OwnerFirstName = garage.Owner == null ? null : garage.Owner.FirstName,
                OwnerMiddleName = garage.Owner == null ? null : garage.Owner.MiddleName,
                IncomeTypeId = (Guid?)null,
                IncomeTypeName = StartingBalanceName,
                AccrualAmount = garage.StartingBalance,
                IncomeAmount = 0m
            });
        var accruals = dbContext.Accruals.AsNoTracking()
            .Where(accrual =>
                !accrual.IsCanceled &&
                garageIds.Contains(accrual.GarageId) &&
                (incomeTypeIds.Count == 0 || incomeTypeIds.Contains(accrual.IncomeTypeId)) &&
                accrual.AccountingMonth >= periodFrom &&
                accrual.AccountingMonth <= periodTo)
            .Select(accrual => new
            {
                accrual.AccountingMonth,
                GarageId = accrual.GarageId,
                GarageNumber = accrual.Garage.Number,
                OwnerLastName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.LastName,
                OwnerFirstName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.FirstName,
                OwnerMiddleName = accrual.Garage.Owner == null ? null : accrual.Garage.Owner.MiddleName,
                IncomeTypeId = (Guid?)accrual.IncomeTypeId,
                IncomeTypeName = accrual.IncomeType.Name,
                AccrualAmount = accrual.Amount,
                IncomeAmount = 0m
            });
        var payments = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.OperationKind == FinancialOperationKinds.Income &&
                operation.GarageId != null &&
                operation.IncomeTypeId != null &&
                garageIds.Contains(operation.GarageId.Value) &&
                (incomeTypeIds.Count == 0 || incomeTypeIds.Contains(operation.IncomeTypeId.Value)) &&
                operation.OperationDate >= periodFrom &&
                operation.OperationDate <= dateTo)
            .Select(operation => new
            {
                operation.AccountingMonth,
                GarageId = operation.GarageId!.Value,
                GarageNumber = operation.Garage!.Number,
                OwnerLastName = operation.Garage.Owner == null ? null : operation.Garage.Owner.LastName,
                OwnerFirstName = operation.Garage.Owner == null ? null : operation.Garage.Owner.FirstName,
                OwnerMiddleName = operation.Garage.Owner == null ? null : operation.Garage.Owner.MiddleName,
                IncomeTypeId = operation.IncomeTypeId,
                IncomeTypeName = operation.IncomeType!.Name,
                AccrualAmount = 0m,
                IncomeAmount = operation.Amount
            });

        var sourceRows = startingBalances.Concat(accruals).Concat(payments);
        var groupedRows = groupAccruals
            ? sourceRows
                .GroupBy(row => new
                {
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName
                })
                .Select(group => new
                {
                    group.Key.AccountingMonth,
                    group.Key.GarageId,
                    group.Key.GarageNumber,
                    group.Key.OwnerLastName,
                    group.Key.OwnerFirstName,
                    group.Key.OwnerMiddleName,
                    IncomeTypeId = (Guid?)null,
                    IncomeTypeName = GroupedIncomeTypeName,
                    AccrualAmount = group.Sum(row => row.AccrualAmount),
                    IncomeAmount = group.Sum(row => row.IncomeAmount)
                })
            : sourceRows
                .GroupBy(row => new
                {
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName,
                    row.IncomeTypeId,
                    row.IncomeTypeName
                })
                .Select(group => new
                {
                    group.Key.AccountingMonth,
                    group.Key.GarageId,
                    group.Key.GarageNumber,
                    group.Key.OwnerLastName,
                    group.Key.OwnerFirstName,
                    group.Key.OwnerMiddleName,
                    group.Key.IncomeTypeId,
                    group.Key.IncomeTypeName,
                    AccrualAmount = group.Sum(row => row.AccrualAmount),
                    IncomeAmount = group.Sum(row => row.IncomeAmount)
                });

        var summary = await groupedRows
            .GroupBy(_ => 1)
            .Select(group => new
            {
                AccrualTotal = group.Sum(row => row.AccrualAmount),
                IncomeTotal = group.Sum(row => row.IncomeAmount),
                RowCount = group.Count()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (summary is null)
        {
            return new GarageReportQueryData(0m, 0m, 0, []);
        }

        var orderedRows = sort.Field switch
        {
            "garageNumber" => sort.Descending ? groupedRows.OrderByDescending(row => row.GarageNumber) : groupedRows.OrderBy(row => row.GarageNumber),
            "ownerName" => sort.Descending
                ? groupedRows.OrderByDescending(row => (row.OwnerLastName ?? string.Empty) + " " + (row.OwnerFirstName ?? string.Empty) + " " + (row.OwnerMiddleName ?? string.Empty))
                : groupedRows.OrderBy(row => (row.OwnerLastName ?? string.Empty) + " " + (row.OwnerFirstName ?? string.Empty) + " " + (row.OwnerMiddleName ?? string.Empty)),
            "incomeTypeName" => sort.Descending ? groupedRows.OrderByDescending(row => row.IncomeTypeName) : groupedRows.OrderBy(row => row.IncomeTypeName),
            "accrualAmount" => sort.Descending ? groupedRows.OrderByDescending(row => row.AccrualAmount) : groupedRows.OrderBy(row => row.AccrualAmount),
            "incomeAmount" => sort.Descending ? groupedRows.OrderByDescending(row => row.IncomeAmount) : groupedRows.OrderBy(row => row.IncomeAmount),
            "difference" => sort.Descending
                ? groupedRows.OrderByDescending(row => row.AccrualAmount - row.IncomeAmount)
                : groupedRows.OrderBy(row => row.AccrualAmount - row.IncomeAmount),
            _ => sort.Descending ? groupedRows.OrderByDescending(row => row.AccountingMonth) : groupedRows.OrderBy(row => row.AccountingMonth)
        };
        var page = orderedRows
            .ThenBy(row => row.GarageNumber)
            .ThenBy(row => row.IncomeTypeName)
            .ThenBy(row => row.AccountingMonth)
            .ThenBy(row => row.GarageId)
            .Skip(offset);
        var rows = await (limit is > 0 ? page.Take(limit.Value) : page).ToListAsync(cancellationToken);

        return new GarageReportQueryData(
            summary.AccrualTotal,
            summary.IncomeTotal,
            summary.RowCount,
            rows.Select(row => new GarageReportQueryRow(
                    row.AccountingMonth,
                    row.GarageId,
                    row.GarageNumber,
                    row.OwnerLastName,
                    row.OwnerFirstName,
                    row.OwnerMiddleName,
                    row.IncomeTypeId,
                    row.IncomeTypeName,
                    row.AccrualAmount,
                    row.IncomeAmount))
                .ToList());
    }

    private async Task<GarageReportQueryData> GetPostgresRowsAsync(
        DateOnly periodFrom,
        DateOnly periodTo,
        DateOnly dateTo,
        string? search,
        IReadOnlySet<Guid> selectedGarageIds,
        IReadOnlySet<Guid> ownerIds,
        IReadOnlySet<Guid> incomeTypeIds,
        bool groupAccruals,
        int offset,
        int? limit,
        ReportSort sort,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var normalizedSearch = search?.Trim().ToLowerInvariant();
        var hasSearch = !string.IsNullOrWhiteSpace(normalizedSearch);
        var sortColumn = sort.Field switch
        {
            "garageNumber" => "garage_number",
            "ownerName" => "owner_name",
            "incomeTypeName" => "income_type_name",
            "accrualAmount" => "accrual_amount",
            "incomeAmount" => "income_amount",
            "difference" => "difference",
            _ => "accounting_month"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var garageClause = selectedGarageIds.Count > 0 ? "AND garage.\"Id\" = ANY(@garage_ids)" : string.Empty;
        var ownerClause = ownerIds.Count > 0 ? "AND garage.\"OwnerId\" = ANY(@owner_ids)" : string.Empty;
        var incomeTypeClause = incomeTypeIds.Count > 0 ? "AND income_type.\"Id\" = ANY(@income_type_ids)" : string.Empty;
        var searchClause = hasSearch
            ? """
              AND (STRPOS(LOWER(garage."Number"), @search) > 0
                   OR STRPOS(LOWER(owner."LastName"), @search) > 0
                   OR STRPOS(LOWER(owner."FirstName"), @search) > 0
                   OR STRPOS(LOWER(owner."MiddleName"), @search) > 0
                   OR STRPOS(LOWER(owner."LastName" || ' ' || owner."FirstName"), @search) > 0
                   OR STRPOS(LOWER(owner."LastName" || ' ' || owner."FirstName" || ' ' || COALESCE(owner."MiddleName", '')), @search) > 0)
              """
            : string.Empty;
        var groupedRowsSql = groupAccruals
            ? """
              SELECT accounting_month, garage_id, garage_number,
                     owner_last_name, owner_first_name, owner_middle_name,
                     COALESCE(owner_last_name, '') || ' ' || COALESCE(owner_first_name, '') || ' ' || COALESCE(owner_middle_name, '') AS owner_name,
                     NULL::uuid AS income_type_id, 'ИТОГО'::text AS income_type_name,
                     SUM(accrual_amount) AS accrual_amount, SUM(income_amount) AS income_amount,
                     SUM(accrual_amount) - SUM(income_amount) AS difference
              FROM source_rows
              GROUP BY accounting_month, garage_id, garage_number,
                       owner_last_name, owner_first_name, owner_middle_name
              """
            : """
              SELECT accounting_month, garage_id, garage_number,
                     owner_last_name, owner_first_name, owner_middle_name,
                     COALESCE(owner_last_name, '') || ' ' || COALESCE(owner_first_name, '') || ' ' || COALESCE(owner_middle_name, '') AS owner_name,
                     income_type_id, income_type_name,
                     SUM(accrual_amount) AS accrual_amount, SUM(income_amount) AS income_amount,
                     SUM(accrual_amount) - SUM(income_amount) AS difference
              FROM source_rows
              GROUP BY accounting_month, garage_id, garage_number,
                       owner_last_name, owner_first_name, owner_middle_name,
                       income_type_id, income_type_name
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_garages AS (
                SELECT garage."Id" AS garage_id,
                       garage."Number" AS garage_number,
                       garage."StartingBalance" AS starting_balance,
                       owner."LastName" AS owner_last_name,
                       owner."FirstName" AS owner_first_name,
                       owner."MiddleName" AS owner_middle_name
                FROM garages garage
                LEFT JOIN owners owner ON owner."Id" = garage."OwnerId"
                WHERE garage."IsArchived" = FALSE
                  {{garageClause}}
                  {{ownerClause}}
                  {{searchClause}}
            ), source_rows AS (
                SELECT @period_from::date AS accounting_month,
                       garage_id, garage_number,
                       owner_last_name, owner_first_name, owner_middle_name,
                       NULL::uuid AS income_type_id,
                       'Стартовый баланс'::text AS income_type_name,
                       starting_balance AS accrual_amount,
                       0::numeric AS income_amount
                FROM filtered_garages
                WHERE @include_starting_balances = TRUE
                  AND starting_balance <> 0
                UNION ALL
                SELECT accrual."AccountingMonth", garage.garage_id, garage.garage_number,
                       garage.owner_last_name, garage.owner_first_name, garage.owner_middle_name,
                       income_type."Id", income_type."Name", accrual."Amount", 0::numeric
                FROM accruals accrual
                INNER JOIN filtered_garages garage ON garage.garage_id = accrual."GarageId"
                INNER JOIN income_types income_type ON income_type."Id" = accrual."IncomeTypeId"
                WHERE accrual."IsCanceled" = FALSE
                  AND accrual."AccountingMonth" >= @period_from::date
                  AND accrual."AccountingMonth" <= @period_to::date
                  {{incomeTypeClause}}
                UNION ALL
                SELECT operation."AccountingMonth", garage.garage_id, garage.garage_number,
                       garage.owner_last_name, garage.owner_first_name, garage.owner_middle_name,
                       income_type."Id", income_type."Name", 0::numeric, operation."Amount"
                FROM financial_operations operation
                INNER JOIN filtered_garages garage ON garage.garage_id = operation."GarageId"
                INNER JOIN income_types income_type ON income_type."Id" = operation."IncomeTypeId"
                WHERE operation."IsCanceled" = FALSE
                  AND operation."OperationKind" = 'income'
                  AND operation."GarageId" IS NOT NULL
                  AND operation."IncomeTypeId" IS NOT NULL
                  AND operation."OperationDate" >= @period_from::date
                  AND operation."OperationDate" <= @date_to::date
                  {{incomeTypeClause}}
            ), grouped_rows AS (
                {{groupedRowsSql}}
            ), page_rows AS (
                SELECT grouped_rows.*,
                       ROW_NUMBER() OVER (
                           ORDER BY {{sortColumn}} {{direction}}, garage_number, income_type_name, accounting_month, garage_id)::int AS row_order
                FROM grouped_rows
                ORDER BY {{sortColumn}} {{direction}}, garage_number, income_type_name, accounting_month, garage_id
                OFFSET @offset
                {{limitClause}}
            )
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder",
                   accounting_month AS "AccountingMonth", garage_id AS "GarageId", garage_number AS "GarageNumber",
                   owner_last_name AS "OwnerLastName", owner_first_name AS "OwnerFirstName", owner_middle_name AS "OwnerMiddleName",
                   income_type_id AS "IncomeTypeId", income_type_name AS "IncomeTypeName",
                   accrual_amount AS "AccrualAmount", income_amount AS "IncomeAmount",
                   0::numeric AS "AccrualTotal", 0::numeric AS "IncomeTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::date, NULL::uuid, NULL::text,
                   NULL::text, NULL::text, NULL::text, NULL::uuid, NULL::text,
                   0::numeric, 0::numeric,
                   COALESCE(SUM(accrual_amount), 0), COALESCE(SUM(income_amount), 0), COUNT(*)::int
            FROM grouped_rows
            ORDER BY "Category", "RowOrder"
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<bool>("include_starting_balances", incomeTypeIds.Count == 0),
            new NpgsqlParameter<DateOnly>("period_from", periodFrom),
            new NpgsqlParameter<DateOnly>("period_to", periodTo),
            new NpgsqlParameter<DateOnly>("date_to", dateTo),
            new NpgsqlParameter<int>("offset", offset)
        };
        if (selectedGarageIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("garage_ids", selectedGarageIds.ToArray()));
        }
        if (ownerIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("owner_ids", ownerIds.ToArray()));
        }
        if (incomeTypeIds.Count > 0)
        {
            parameters.Add(new NpgsqlParameter<Guid[]>("income_type_ids", incomeTypeIds.ToArray()));
        }
        if (hasSearch)
        {
            parameters.Add(new NpgsqlParameter<string>("search", normalizedSearch!));
        }
        if (limit is > 0)
        {
            parameters.Add(new NpgsqlParameter<int>("limit", limit.Value));
        }

        var combinedRows = await dbContext.Database
            .SqlQueryRaw<GarageReportCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = combinedRows.Single(row => row.Category == TotalsCategory);
        var rows = combinedRows
            .Where(row => row.Category == PageCategory)
            .Select(row => new GarageReportQueryRow(
                row.AccountingMonth!.Value,
                row.GarageId!.Value,
                row.GarageNumber!,
                row.OwnerLastName,
                row.OwnerFirstName,
                row.OwnerMiddleName,
                row.IncomeTypeId,
                row.IncomeTypeName!,
                row.AccrualAmount,
                row.IncomeAmount))
            .ToList();
        return new GarageReportQueryData(totals.AccrualTotal, totals.IncomeTotal, totals.RowCount, rows);
    }

    private sealed record GarageReportCombinedQueryRow(
        int Category,
        int RowOrder,
        DateOnly? AccountingMonth,
        Guid? GarageId,
        string? GarageNumber,
        string? OwnerLastName,
        string? OwnerFirstName,
        string? OwnerMiddleName,
        Guid? IncomeTypeId,
        string? IncomeTypeName,
        decimal AccrualAmount,
        decimal IncomeAmount,
        decimal AccrualTotal,
        decimal IncomeTotal,
        int RowCount);

    private bool IsNpgsql() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ?? false;
}
