using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFundChangeReportQuery(GarageBalanceDbContext dbContext) : IFundChangeReportQuery
{
    public Task<FundChangeReportData> GetFundChangesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? search,
        int offset,
        int? limit,
        CancellationToken cancellationToken) =>
        GetFundChangesAsync(dateFrom, dateTo, search, offset, limit, new ReportSort("date", false), cancellationToken);

    public async Task<FundChangeReportData> GetFundChangesAsync(
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
            return await GetPostgresFundChangesAsync(fromUtc, toExclusiveUtc, search, offset, limit, sort, cancellationToken);
        }

        var query = dbContext.FundOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        IReadOnlyList<FundChangeReportQueryRow> rows;
        int rowCount;
        decimal depositTotal;
        decimal withdrawalTotal;
        if (IsSqliteProvider())
        {
            var filtered = (await ProjectRows(dbContext.FundOperations.AsNoTracking()).ToListAsync(cancellationToken))
                .Where(row =>
                    !row.IsCanceled &&
                    row.CreatedAtUtc >= fromUtc &&
                    row.CreatedAtUtc < toExclusiveUtc);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim();
                filtered = filtered.Where(row =>
                    row.FundName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.OperationKind.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    row.Reason.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            rowCount = filteredList.Count;
            depositTotal = filteredList.Where(row => row.OperationKind == FundOperationKinds.Deposit).Sum(row => row.Amount);
            withdrawalTotal = filteredList.Where(row => row.OperationKind == FundOperationKinds.Withdraw).Sum(row => row.Amount);
            rows = ApplyPage(
                    ApplySort(filteredList, sort).ThenByDescending(row => row.Id),
                    offset,
                    limit)
                .Select(row => row.ToQueryRow())
                .ToList();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim().ToLower();
                query = query.Where(operation =>
                    operation.Fund.Name.ToLower().Contains(normalizedSearch) ||
                    operation.OperationKind.ToLower().Contains(normalizedSearch) ||
                    operation.Reason.ToLower().Contains(normalizedSearch));
            }

            var totalsByKind = await query
                .GroupBy(operation => operation.OperationKind)
                .Select(group => new
                {
                    OperationKind = group.Key,
                    RowCount = group.Count(),
                    Total = group.Sum(operation => operation.Amount)
                })
                .ToListAsync(cancellationToken);
            rowCount = totalsByKind.Sum(row => row.RowCount);
            depositTotal = totalsByKind
                .Where(row => row.OperationKind == FundOperationKinds.Deposit)
                .Sum(row => row.Total);
            withdrawalTotal = totalsByKind
                .Where(row => row.OperationKind == FundOperationKinds.Withdraw)
                .Sum(row => row.Total);
            var projected = ProjectRows(query);
            var ordered = ApplySort(projected, sort).ThenByDescending(row => row.Id);
            var pageRows = await ApplyPage(ordered, offset, limit).ToListAsync(cancellationToken);
            rows = pageRows.Select(row => row.ToQueryRow()).ToList();
        }

        return new FundChangeReportData(rows, depositTotal, withdrawalTotal, rowCount);
    }

    private async Task<FundChangeReportData> GetPostgresFundChangesAsync(
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
            "fundName" => "fund_name",
            "changeName" => "change_name",
            "amount" => "amount",
            "balanceBefore" => "balance_before",
            "balanceAfter" => "balance_after",
            "actorDisplayName" => "actor_display_name",
            "reason" => "reason",
            _ => "created_at_utc"
        };
        var direction = sort.Descending ? "DESC" : "ASC";
        var searchClause = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : """
              AND (LOWER(fund."Name") LIKE '%' || @search || '%'
                   OR LOWER(operation."OperationKind") LIKE '%' || @search || '%'
                   OR LOWER(operation."Reason") LIKE '%' || @search || '%')
              """;
        var limitClause = limit is > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $$"""
            WITH filtered_rows AS (
                SELECT operation."Id" AS id,
                       operation."FundId" AS fund_id,
                       fund."Name" AS fund_name,
                       operation."CreatedAtUtc" AS created_at_utc,
                       operation."OperationKind" AS operation_kind,
                       CASE operation."OperationKind"
                           WHEN 'deposit' THEN 'Пополнение'
                           WHEN 'withdraw' THEN 'Изъятие'
                           ELSE operation."OperationKind"
                       END AS change_name,
                       operation."Amount" AS amount,
                       operation."BalanceBefore" AS balance_before,
                       operation."BalanceAfter" AS balance_after,
                       operation."ActorUserId" AS actor_user_id,
                       actor."DisplayName" AS actor_display_name,
                       operation."Reason" AS reason
                FROM fund_operations operation
                INNER JOIN funds fund ON fund."Id" = operation."FundId"
                LEFT JOIN app_users actor ON actor."Id" = operation."ActorUserId"
                WHERE operation."IsCanceled" = FALSE
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
            SELECT {{PageCategory}} AS "Category", row_order AS "RowOrder", id AS "Id", fund_id AS "FundId",
                   fund_name AS "FundName", created_at_utc AS "CreatedAtUtc", operation_kind AS "OperationKind",
                   amount AS "Amount", balance_before AS "BalanceBefore", balance_after AS "BalanceAfter",
                   actor_user_id AS "ActorUserId", actor_display_name AS "ActorDisplayName", reason AS "Reason",
                   0::numeric AS "DepositTotal", 0::numeric AS "WithdrawalTotal", 0 AS "RowCount"
            FROM page_rows
            UNION ALL
            SELECT {{TotalsCategory}}, 0, NULL::uuid, NULL::uuid, ''::text, NULL::timestamptz, ''::text,
                   0::numeric, 0::numeric, 0::numeric, NULL::uuid, NULL::text, ''::text,
                   COALESCE(SUM(amount) FILTER (WHERE operation_kind = 'deposit'), 0),
                   COALESCE(SUM(amount) FILTER (WHERE operation_kind = 'withdraw'), 0),
                   COUNT(*)::int
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

        var result = await dbContext.Database
            .SqlQueryRaw<FundChangeCombinedQueryRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        var totals = result.Single(row => row.Category == TotalsCategory);
        var rows = result
            .Where(row => row.Category == PageCategory)
            .Select(row => new FundChangeReportQueryRow(
                row.Id!.Value,
                row.FundId!.Value,
                row.FundName,
                row.CreatedAtUtc!.Value,
                row.OperationKind,
                row.Amount,
                row.BalanceBefore,
                row.BalanceAfter,
                row.ActorUserId,
                row.ActorDisplayName,
                row.Reason))
            .ToList();
        return new FundChangeReportData(rows, totals.DepositTotal, totals.WithdrawalTotal, totals.RowCount);
    }

    private IQueryable<FundChangeProjectionRow> ProjectRows(IQueryable<FundOperation> operations) =>
        from operation in operations
        join actor in dbContext.Users.AsNoTracking()
            on operation.ActorUserId equals (Guid?)actor.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new FundChangeProjectionRow
        {
            Id = operation.Id,
            FundId = operation.FundId,
            FundName = operation.Fund.Name,
            CreatedAtUtc = operation.CreatedAtUtc,
            OperationKind = operation.OperationKind,
            ChangeName = operation.OperationKind == FundOperationKinds.Deposit
                ? "Пополнение"
                : operation.OperationKind == FundOperationKinds.Withdraw
                    ? "Изъятие"
                    : operation.OperationKind,
            Amount = operation.Amount,
            BalanceBefore = operation.BalanceBefore,
            BalanceAfter = operation.BalanceAfter,
            ActorUserId = operation.ActorUserId,
            ActorDisplayName = actor == null ? null : actor.DisplayName,
            Reason = operation.Reason,
            IsCanceled = operation.IsCanceled
        };

    private static IOrderedQueryable<FundChangeProjectionRow> ApplySort(IQueryable<FundChangeProjectionRow> query, ReportSort sort) =>
        sort.Field switch
        {
            "fundName" => sort.Descending ? query.OrderByDescending(row => row.FundName) : query.OrderBy(row => row.FundName),
            "changeName" => sort.Descending ? query.OrderByDescending(row => row.ChangeName) : query.OrderBy(row => row.ChangeName),
            "amount" => sort.Descending ? query.OrderByDescending(row => row.Amount) : query.OrderBy(row => row.Amount),
            "balanceBefore" => sort.Descending ? query.OrderByDescending(row => row.BalanceBefore) : query.OrderBy(row => row.BalanceBefore),
            "balanceAfter" => sort.Descending ? query.OrderByDescending(row => row.BalanceAfter) : query.OrderBy(row => row.BalanceAfter),
            "actorDisplayName" => sort.Descending ? query.OrderByDescending(row => row.ActorDisplayName) : query.OrderBy(row => row.ActorDisplayName),
            "reason" => sort.Descending ? query.OrderByDescending(row => row.Reason) : query.OrderBy(row => row.Reason),
            _ => sort.Descending ? query.OrderByDescending(row => row.CreatedAtUtc) : query.OrderBy(row => row.CreatedAtUtc)
        };

    private static IOrderedEnumerable<FundChangeProjectionRow> ApplySort(IEnumerable<FundChangeProjectionRow> rows, ReportSort sort) =>
        sort.Field switch
        {
            "fundName" => sort.Descending ? rows.OrderByDescending(row => row.FundName, StringComparer.Ordinal) : rows.OrderBy(row => row.FundName, StringComparer.Ordinal),
            "changeName" => sort.Descending ? rows.OrderByDescending(row => row.ChangeName, StringComparer.Ordinal) : rows.OrderBy(row => row.ChangeName, StringComparer.Ordinal),
            "amount" => sort.Descending ? rows.OrderByDescending(row => row.Amount) : rows.OrderBy(row => row.Amount),
            "balanceBefore" => sort.Descending ? rows.OrderByDescending(row => row.BalanceBefore) : rows.OrderBy(row => row.BalanceBefore),
            "balanceAfter" => sort.Descending ? rows.OrderByDescending(row => row.BalanceAfter) : rows.OrderBy(row => row.BalanceAfter),
            "actorDisplayName" => sort.Descending ? rows.OrderByDescending(row => row.ActorDisplayName, StringComparer.Ordinal) : rows.OrderBy(row => row.ActorDisplayName, StringComparer.Ordinal),
            "reason" => sort.Descending ? rows.OrderByDescending(row => row.Reason, StringComparer.Ordinal) : rows.OrderBy(row => row.Reason, StringComparer.Ordinal),
            _ => sort.Descending ? rows.OrderByDescending(row => row.CreatedAtUtc) : rows.OrderBy(row => row.CreatedAtUtc)
        };

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

    private sealed class FundChangeProjectionRow
    {
        public Guid Id { get; init; }
        public Guid FundId { get; init; }
        public string FundName { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public string OperationKind { get; init; } = string.Empty;
        public string ChangeName { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal BalanceBefore { get; init; }
        public decimal BalanceAfter { get; init; }
        public Guid? ActorUserId { get; init; }
        public string? ActorDisplayName { get; init; }
        public string Reason { get; init; } = string.Empty;
        public bool IsCanceled { get; init; }

        public FundChangeReportQueryRow ToQueryRow() =>
            new(Id, FundId, FundName, CreatedAtUtc, OperationKind, Amount, BalanceBefore, BalanceAfter, ActorUserId, ActorDisplayName, Reason);
    }

    private sealed record FundChangeCombinedQueryRow(
        int Category,
        int RowOrder,
        Guid? Id,
        Guid? FundId,
        string FundName,
        DateTimeOffset? CreatedAtUtc,
        string OperationKind,
        decimal Amount,
        decimal BalanceBefore,
        decimal BalanceAfter,
        Guid? ActorUserId,
        string? ActorDisplayName,
        string Reason,
        decimal DepositTotal,
        decimal WithdrawalTotal,
        int RowCount);
}
