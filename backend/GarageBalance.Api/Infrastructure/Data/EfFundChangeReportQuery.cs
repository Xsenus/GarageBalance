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
        int? limit,
        CancellationToken cancellationToken)
    {
        var fromUtc = new DateTimeOffset(dateFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toExclusiveUtc = new DateTimeOffset(dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var query = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation =>
                !operation.IsCanceled &&
                operation.CreatedAtUtc >= fromUtc &&
                operation.CreatedAtUtc < toExclusiveUtc);

        IReadOnlyList<FundOperation> operations;
        int rowCount;
        decimal depositTotal;
        decimal withdrawalTotal;
        if (IsSqliteProvider())
        {
            var filtered = (await dbContext.FundOperations.AsNoTracking()
                    .Include(operation => operation.Fund)
                    .ToListAsync(cancellationToken))
                .Where(operation =>
                    !operation.IsCanceled &&
                    operation.CreatedAtUtc >= fromUtc &&
                    operation.CreatedAtUtc < toExclusiveUtc);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalizedSearch = search.Trim();
                filtered = filtered.Where(operation =>
                    operation.Fund.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    operation.OperationKind.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                    operation.Reason.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            rowCount = filteredList.Count;
            depositTotal = filteredList.Where(operation => operation.OperationKind == FundOperationKinds.Deposit).Sum(operation => operation.Amount);
            withdrawalTotal = filteredList.Where(operation => operation.OperationKind == FundOperationKinds.Withdraw).Sum(operation => operation.Amount);
            operations = ApplyLimit(
                    filteredList.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Fund.Name),
                    limit)
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

            rowCount = await query.CountAsync(cancellationToken);
            depositTotal = await query.Where(operation => operation.OperationKind == FundOperationKinds.Deposit)
                .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
            withdrawalTotal = await query.Where(operation => operation.OperationKind == FundOperationKinds.Withdraw)
                .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
            var ordered = query.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Fund.Name);
            operations = await ApplyLimit(ordered, limit).ToListAsync(cancellationToken);
        }

        var actorIds = operations.Where(operation => operation.ActorUserId.HasValue)
            .Select(operation => operation.ActorUserId!.Value)
            .Distinct()
            .ToList();
        var usersById = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Users.AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
        return new FundChangeReportData(operations, depositTotal, withdrawalTotal, rowCount, usersById);
    }

    private static IQueryable<T> ApplyLimit<T>(IQueryable<T> query, int? limit) =>
        limit is > 0 ? query.Take(limit.Value) : query;

    private static IEnumerable<T> ApplyLimit<T>(IEnumerable<T> items, int? limit) =>
        limit is > 0 ? items.Take(limit.Value) : items;

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
