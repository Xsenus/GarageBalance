using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFundRepository(GarageBalanceDbContext dbContext) : IFundRepository
{
    public async Task<IReadOnlyList<Fund>> GetFundsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Funds.AsNoTracking()
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FundOperation>> GetRecentOperationsAsync(
        int limit,
        bool includeCanceled,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation => includeCanceled || !operation.IsCanceled);
        if (IsSqliteProvider())
        {
            return (await query.ToListAsync(cancellationToken))
                .OrderByDescending(operation => operation.CreatedAtUtc)
                .ThenByDescending(operation => operation.Id)
                .Take(limit)
                .ToList();
        }

        return await query
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<FundOperationPageData> GetOperationsPageAsync(
        int offset,
        int limit,
        bool includeCanceled,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.AsNoTracking()
            .Include(operation => operation.Fund)
            .Where(operation => includeCanceled || !operation.IsCanceled);

        if (IsSqliteProvider())
        {
            var operations = await query.ToListAsync(cancellationToken);
            return new FundOperationPageData(
                operations.OrderByDescending(operation => operation.CreatedAtUtc)
                    .ThenByDescending(operation => operation.Id)
                    .Skip(offset)
                    .Take(limit)
                    .ToList(),
                operations.Count);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new FundOperationPageData(items, totalCount);
    }

    public Task<Fund?> FindFundForUpdateAsync(Guid fundId, CancellationToken cancellationToken)
    {
        return dbContext.Funds.SingleOrDefaultAsync(fund => fund.Id == fundId, cancellationToken);
    }

    public Task<FundOperation?> FindOperationForUpdateAsync(Guid operationId, CancellationToken cancellationToken)
    {
        return dbContext.FundOperations
            .Include(operation => operation.Fund)
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
    }

    public async Task<FundTotalsData> GetTotalsAsync(CancellationToken cancellationToken)
    {
        var totals = await dbContext.Funds.AsNoTracking()
            .Select(_ => new
            {
                IncomeTotal = dbContext.FinancialOperations.AsNoTracking()
                    .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Income)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m,
                ExpenseTotal = dbContext.FinancialOperations.AsNoTracking()
                    .Where(operation => !operation.IsCanceled && operation.OperationKind == FinancialOperationKinds.Expense)
                    .Sum(operation => (decimal?)operation.Amount) ?? 0m,
                AllocatedFundTotal = dbContext.Funds.AsNoTracking()
                    .Sum(fund => (decimal?)fund.Balance) ?? 0m
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new FundTotalsData(0m, 0m, 0m)
            : new FundTotalsData(totals.IncomeTotal, totals.ExpenseTotal, totals.AllocatedFundTotal);
    }

    public async Task<decimal> GetActiveDepositTotalAsync(CancellationToken cancellationToken)
    {
        return await dbContext.FundOperations.AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.OperationKind == FundOperationKinds.Deposit)
            .SumAsync(operation => (decimal?)operation.Amount, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<FundOperation>> GetOperationsOrderedAsync(
        Guid fundId,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.Where(operation => operation.FundId == fundId);
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return IsSqliteProvider()
            ? (await query.ToListAsync(cancellationToken)).OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Id).ToList()
            : await query.OrderBy(operation => operation.CreatedAtUtc).ThenBy(operation => operation.Id).ToListAsync(cancellationToken);
    }

    public void AddFund(Fund fund) => dbContext.Funds.Add(fund);

    public void AddOperation(FundOperation operation) => dbContext.FundOperations.Add(operation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
}
