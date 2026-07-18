using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Data.Common;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFundRepository(GarageBalanceDbContext dbContext) : IFundRepository
{
    private const long FundAllocationLockKey = 0x474246554E44;

    public async Task<IAsyncDisposable> AcquireAllocationLockAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await ExecuteAdvisoryLockCommandAsync(
                connection,
                "SELECT pg_advisory_lock(@lock_key)",
                cancellationToken);
            return new PostgreSqlAdvisoryLockLease(connection, closeConnection);
        }
        catch
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }

            throw;
        }
    }

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

    public Task<FundOperation?> FindIncomeAssignmentForUpdateAsync(
        Guid sourceFinancialOperationId,
        CancellationToken cancellationToken)
    {
        return dbContext.FundOperations
            .Include(operation => operation.Fund)
            .SingleOrDefaultAsync(
                operation => operation.SourceFinancialOperationId == sourceFinancialOperationId,
                cancellationToken);
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

    private static async Task ExecuteAdvisoryLockCommandAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = FundAllocationLockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class PostgreSqlAdvisoryLockLease(
        DbConnection connection,
        bool closeConnection) : IAsyncDisposable
    {
        private bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                await ExecuteAdvisoryLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_unlock(@lock_key)",
                    CancellationToken.None);
            }
            finally
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
