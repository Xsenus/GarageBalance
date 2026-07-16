using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFundChangeReportQuery(GarageBalanceDbContext dbContext) : IFundChangeReportQuery
{
    public async Task<FundChangeReportData> GetFundChangesAsync(
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
                    filteredList.OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.FundName).ThenBy(row => row.Id),
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
            var ordered = query.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Fund.Name).ThenBy(operation => operation.Id);
            var pageRows = await ProjectRows(ApplyPage(ordered, offset, limit)).ToListAsync(cancellationToken);
            rows = pageRows.Select(row => row.ToQueryRow()).ToList();
        }

        return new FundChangeReportData(rows, depositTotal, withdrawalTotal, rowCount);
    }

    private IQueryable<FundChangeProjectionRow> ProjectRows(IQueryable<FundOperation> operations) =>
        from operation in operations
        join actor in dbContext.Users.AsNoTracking()
            on operation.ActorUserId equals (Guid?)actor.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new FundChangeProjectionRow(
            operation.Id,
            operation.FundId,
            operation.Fund.Name,
            operation.CreatedAtUtc,
            operation.OperationKind,
            operation.Amount,
            operation.BalanceBefore,
            operation.BalanceAfter,
            operation.ActorUserId,
            actor == null ? null : actor.DisplayName,
            operation.Reason,
            operation.IsCanceled);

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

    private sealed record FundChangeProjectionRow(
        Guid Id,
        Guid FundId,
        string FundName,
        DateTimeOffset CreatedAtUtc,
        string OperationKind,
        decimal Amount,
        decimal BalanceBefore,
        decimal BalanceAfter,
        Guid? ActorUserId,
        string? ActorDisplayName,
        string Reason,
        bool IsCanceled)
    {
        public FundChangeReportQueryRow ToQueryRow() =>
            new(Id, FundId, FundName, CreatedAtUtc, OperationKind, Amount, BalanceBefore, BalanceAfter, ActorUserId, ActorDisplayName, Reason);
    }
}
