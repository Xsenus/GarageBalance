using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Funds;
using GarageBalance.Api.Domain.Dictionaries;
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
            .Where(fund => !fund.IsArchived)
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fund>> GetFundsForUpdateAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Funds
            .Where(fund => !fund.IsArchived)
            .OrderBy(fund => fund.SortOrder)
            .ThenBy(fund => fund.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> FundNameExistsAsync(
        Guid? excludedFundId,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        return dbContext.Funds.AnyAsync(
            fund => !fund.IsArchived &&
                fund.NormalizedName == normalizedName &&
                (!excludedFundId.HasValue || fund.Id != excludedFundId.Value),
            cancellationToken);
    }

    public Task<bool> ActiveFundExistsAsync(Guid fundId, CancellationToken cancellationToken)
    {
        return dbContext.Funds.AsNoTracking()
            .AnyAsync(fund => fund.Id == fundId && !fund.IsArchived, cancellationToken);
    }

    public async Task<IReadOnlyList<FundLinkedServiceData>> GetLinkedServicesAsync(
        IReadOnlyCollection<Guid> fundIds,
        CancellationToken cancellationToken)
    {
        if (fundIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Suppliers.AsNoTracking()
            .Where(supplier =>
                !supplier.IsArchived &&
                supplier.ChargeServiceSettingId.HasValue &&
                !supplier.ChargeServiceSetting!.IsArchived &&
                supplier.ExpenseFundId.HasValue &&
                fundIds.Contains(supplier.ExpenseFundId.Value))
            .OrderBy(supplier => supplier.ChargeServiceSetting!.Name)
            .ThenBy(supplier => supplier.ChargeServiceSettingId)
            .Select(supplier => new FundLinkedServiceData(
                supplier.ExpenseFundId!.Value,
                supplier.ChargeServiceSettingId!.Value,
                supplier.ChargeServiceSetting!.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncomeType>> GetIncomeTypesForFundUpdateAsync(
        Guid fundId,
        CancellationToken cancellationToken)
    {
        return await dbContext.IncomeTypes
            .Where(incomeType => incomeType.DestinationFundId == fundId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FundOperation>> GetRecentOperationsAsync(
        int limit,
        bool includeCanceled,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.AsNoTracking()
            .Where(operation =>
                operation.SourceFinancialOperationId == null &&
                (includeCanceled || !operation.IsCanceled));
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresRecentOperationsAsync(query, limit, cancellationToken);
        }

        var queryWithFund = query.Include(operation => operation.Fund);
        if (IsSqliteProvider())
        {
            return (await queryWithFund.ToListAsync(cancellationToken))
                .OrderByDescending(operation => operation.CreatedAtUtc)
                .ThenByDescending(operation => operation.Id)
                .Take(limit)
                .ToList();
        }

        return await queryWithFund
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<FundOperation>> GetPostgresRecentOperationsAsync(
        IQueryable<FundOperation> query,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await query
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Take(limit)
            .Select(operation => new
            {
                operation.Id,
                operation.FundId,
                FundName = operation.Fund.Name,
                operation.SourceFinancialOperationId,
                operation.OperationKind,
                operation.Amount,
                operation.BalanceBefore,
                operation.BalanceAfter,
                operation.Reason,
                operation.IsCanceled,
                operation.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new FundOperation
        {
            Id = row.Id,
            FundId = row.FundId,
            Fund = new Fund { Id = row.FundId, Name = row.FundName },
            SourceFinancialOperationId = row.SourceFinancialOperationId,
            OperationKind = row.OperationKind,
            Amount = row.Amount,
            BalanceBefore = row.BalanceBefore,
            BalanceAfter = row.BalanceAfter,
            Reason = row.Reason,
            IsCanceled = row.IsCanceled,
            CreatedAtUtc = row.CreatedAtUtc
        }).ToList();
    }

    public async Task<FundOperationPageData> GetOperationsPageAsync(
        int offset,
        int limit,
        bool includeCanceled,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.AsNoTracking()
            .Where(operation =>
                operation.SourceFinancialOperationId == null &&
                (includeCanceled || !operation.IsCanceled));

        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresOperationsPageAsync(query, offset, limit, cancellationToken);
        }

        var queryWithFund = query.Include(operation => operation.Fund);

        if (IsSqliteProvider())
        {
            var operations = await queryWithFund.ToListAsync(cancellationToken);
            return new FundOperationPageData(
                operations.OrderByDescending(operation => operation.CreatedAtUtc)
                    .ThenByDescending(operation => operation.Id)
                    .Skip(offset)
                    .Take(limit)
                    .ToList(),
                operations.Count);
        }

        var totalCount = await queryWithFund.CountAsync(cancellationToken);
        var items = await queryWithFund
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new FundOperationPageData(items, totalCount);
    }

    private async Task<FundOperationPageData> GetPostgresOperationsPageAsync(
        IQueryable<FundOperation> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderByDescending(operation => operation.CreatedAtUtc)
            .ThenByDescending(operation => operation.Id)
            .Skip(offset)
            .Take(limit)
            .Select(operation => new
            {
                Category = PageCategory,
                Id = (Guid?)operation.Id,
                FundId = (Guid?)operation.FundId,
                FundName = (string?)operation.Fund.Name,
                SourceFinancialOperationId = (Guid?)operation.SourceFinancialOperationId,
                OperationKind = (string?)operation.OperationKind,
                Amount = (decimal?)operation.Amount,
                BalanceBefore = (decimal?)operation.BalanceBefore,
                BalanceAfter = (decimal?)operation.BalanceAfter,
                Reason = (string?)operation.Reason,
                IsCanceled = (bool?)operation.IsCanceled,
                CreatedAtUtc = (DateTimeOffset?)operation.CreatedAtUtc,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new
            {
                Category = TotalsCategory,
                Id = (Guid?)null,
                FundId = (Guid?)null,
                FundName = (string?)null,
                SourceFinancialOperationId = (Guid?)null,
                OperationKind = (string?)null,
                Amount = (decimal?)null,
                BalanceBefore = (decimal?)null,
                BalanceAfter = (decimal?)null,
                Reason = (string?)null,
                IsCanceled = (bool?)null,
                CreatedAtUtc = (DateTimeOffset?)null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.CreatedAtUtc)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(row => new FundOperation
            {
                Id = row.Id!.Value,
                FundId = row.FundId!.Value,
                Fund = new Fund { Id = row.FundId.Value, Name = row.FundName! },
                SourceFinancialOperationId = row.SourceFinancialOperationId,
                OperationKind = row.OperationKind!,
                Amount = row.Amount!.Value,
                BalanceBefore = row.BalanceBefore!.Value,
                BalanceAfter = row.BalanceAfter!.Value,
                Reason = row.Reason!,
                IsCanceled = row.IsCanceled!.Value,
                CreatedAtUtc = row.CreatedAtUtc!.Value
            })
            .ToList();
        return new FundOperationPageData(items, totalCount);
    }

    public Task<Fund?> FindFundForUpdateAsync(Guid fundId, CancellationToken cancellationToken)
    {
        return dbContext.Funds.SingleOrDefaultAsync(
            fund => fund.Id == fundId && !fund.IsArchived,
            cancellationToken);
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
        const int financialTotalsCategory = 1;
        const int allocatedFundTotalsCategory = 2;
        const int balanceAdjustmentTotalsCategory = 3;
        var financialTotalsQuery = dbContext.FinancialOperations.AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                (operation.OperationKind == FinancialOperationKinds.Income ||
                 operation.OperationKind == FinancialOperationKinds.Expense))
            .GroupBy(_ => financialTotalsCategory)
            .Select(group => new
            {
                Category = financialTotalsCategory,
                IncomeTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Income ? operation.Amount : 0m),
                ExpenseTotal = group.Sum(operation =>
                    operation.OperationKind == FinancialOperationKinds.Expense ? operation.Amount : 0m),
                AllocatedFundTotal = 0m,
                BalanceAdjustmentTotal = 0m
            });
        var allocatedFundTotalsQuery = dbContext.Funds.AsNoTracking()
            .Where(fund => !fund.IsArchived)
            .GroupBy(_ => allocatedFundTotalsCategory)
            .Select(group => new
            {
                Category = allocatedFundTotalsCategory,
                IncomeTotal = 0m,
                ExpenseTotal = 0m,
                AllocatedFundTotal = group.Sum(fund => fund.Balance),
                BalanceAdjustmentTotal = 0m
            });
        var balanceAdjustmentTotalsQuery = dbContext.CashBankBalanceOperations.AsNoTracking()
            .GroupBy(_ => balanceAdjustmentTotalsCategory)
            .Select(group => new
            {
                Category = balanceAdjustmentTotalsCategory,
                IncomeTotal = 0m,
                ExpenseTotal = 0m,
                AllocatedFundTotal = 0m,
                BalanceAdjustmentTotal = group.Sum(operation =>
                    operation.Direction == CashBankBalanceDirections.Increase ? operation.Amount : -operation.Amount)
            });
        var totals = await financialTotalsQuery
            .Concat(allocatedFundTotalsQuery)
            .Concat(balanceAdjustmentTotalsQuery)
            .ToListAsync(cancellationToken);

        var financialTotals = totals.FirstOrDefault(item => item.Category == financialTotalsCategory);
        var allocatedFundTotals = totals.FirstOrDefault(item => item.Category == allocatedFundTotalsCategory);
        var balanceAdjustmentTotals = totals.FirstOrDefault(item => item.Category == balanceAdjustmentTotalsCategory);

        return new FundTotalsData(
            financialTotals?.IncomeTotal ?? 0m,
            financialTotals?.ExpenseTotal ?? 0m,
            allocatedFundTotals?.AllocatedFundTotal ?? 0m,
            balanceAdjustmentTotals?.BalanceAdjustmentTotal ?? 0m);
    }

    public async Task<decimal> GetAvailableToDistributeAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
        {
            const string sql = """
                WITH pool_events AS (
                    SELECT operation."CreatedAtUtc", operation."Id",
                        CASE
                            WHEN operation."OperationKind" = 'income' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END AS delta
                    FROM financial_operations AS operation
                    WHERE NOT operation."IsCanceled"
                      AND operation."OperationKind" IN ('income', 'expense')
                      AND NOT EXISTS (
                          SELECT 1
                          FROM fund_operations AS fund_operation
                          WHERE fund_operation."SourceFinancialOperationId" = operation."Id"
                            AND NOT fund_operation."IsCanceled")

                    UNION ALL

                    SELECT operation."CreatedAtUtc", operation."Id",
                        CASE
                            WHEN operation."OperationKind" = 'withdraw' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END AS delta
                    FROM fund_operations AS operation
                    WHERE NOT operation."IsCanceled"
                      AND operation."SourceFinancialOperationId" IS NULL

                    UNION ALL

                    SELECT operation."CreatedAtUtc", operation."Id",
                        CASE
                            WHEN operation."Direction" = 'increase' THEN operation."Amount"
                            ELSE -operation."Amount"
                        END AS delta
                    FROM cash_bank_balance_operations AS operation
                ),
                running_pool AS (
                    SELECT delta,
                        SUM(delta) OVER (ORDER BY "CreatedAtUtc", "Id") AS running_total
                    FROM pool_events
                )
                SELECT ROUND(GREATEST(
                    COALESCE(SUM(delta), 0) - LEAST(COALESCE(MIN(running_total), 0), 0),
                    0), 2) AS "Value"
                FROM running_pool
                """;

            return await dbContext.Database.SqlQueryRaw<decimal>(sql).SingleAsync(cancellationToken);
        }

        var linkedFinancialOperationIds = await dbContext.FundOperations
            .AsNoTracking()
            .Where(operation => !operation.IsCanceled && operation.SourceFinancialOperationId.HasValue)
            .Select(operation => operation.SourceFinancialOperationId!.Value)
            .ToListAsync(cancellationToken);
        var financialEvents = await dbContext.FinancialOperations
            .AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                (operation.OperationKind == FinancialOperationKinds.Income ||
                 operation.OperationKind == FinancialOperationKinds.Expense) &&
                !linkedFinancialOperationIds.Contains(operation.Id))
            .Select(operation => new PoolEvent(
                operation.CreatedAtUtc,
                operation.Id,
                operation.OperationKind == FinancialOperationKinds.Income
                    ? operation.Amount
                    : -operation.Amount))
            .ToListAsync(cancellationToken);
        var manualFundEvents = await dbContext.FundOperations
            .AsNoTracking()
            .Where(operation =>
                !operation.IsCanceled &&
                !operation.SourceFinancialOperationId.HasValue)
            .Select(operation => new PoolEvent(
                operation.CreatedAtUtc,
                operation.Id,
                operation.OperationKind == FundOperationKinds.Withdraw
                    ? operation.Amount
                    : -operation.Amount))
            .ToListAsync(cancellationToken);
        var cashBankEvents = await dbContext.CashBankBalanceOperations
            .AsNoTracking()
            .Select(operation => new PoolEvent(
                operation.CreatedAtUtc,
                operation.Id,
                operation.Direction == CashBankBalanceDirections.Increase
                    ? operation.Amount
                    : -operation.Amount))
            .ToListAsync(cancellationToken);

        var balance = 0m;
        foreach (var poolEvent in financialEvents
                     .Concat(manualFundEvents)
                     .Concat(cashBankEvents)
                     .OrderBy(poolEvent => poolEvent.CreatedAtUtc)
                     .ThenBy(poolEvent => poolEvent.Id))
        {
            balance = Math.Max(balance + poolEvent.Amount, 0m);
        }

        return MoneyMath.RoundMoney(balance);
    }

    public async Task<IReadOnlyList<FundOperation>> GetOperationsFromAsync(
        Guid fundId,
        Guid operationId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.Where(operation => operation.FundId == fundId);
        var ordered = IsSqliteProvider()
            ? (await query.ToListAsync(cancellationToken))
                .Where(operation => operation.CreatedAtUtc >= createdAtUtc)
                .OrderBy(operation => operation.CreatedAtUtc)
                .ThenBy(operation => operation.Id)
                .ToList()
            : await query.Where(operation => operation.CreatedAtUtc >= createdAtUtc)
                .OrderBy(operation => operation.CreatedAtUtc)
                .ThenBy(operation => operation.Id)
                .ToListAsync(cancellationToken);
        var startIndex = ordered.FindIndex(operation => operation.Id == operationId);
        if (startIndex < 0)
        {
            return [];
        }

        return ordered.GetRange(startIndex, ordered.Count - startIndex);
    }

    public async Task<IReadOnlyList<FundOperation>> GetOperationsSinceAsync(
        Guid fundId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var query = dbContext.FundOperations.Where(operation => operation.FundId == fundId);

        return IsSqliteProvider()
            ? (await query.ToListAsync(cancellationToken))
                .Where(operation => operation.CreatedAtUtc >= createdAtUtc)
                .OrderBy(operation => operation.CreatedAtUtc)
                .ThenBy(operation => operation.Id)
                .ToList()
            : await query.Where(operation => operation.CreatedAtUtc >= createdAtUtc)
                .OrderBy(operation => operation.CreatedAtUtc)
                .ThenBy(operation => operation.Id)
                .ToListAsync(cancellationToken);
    }

    public void AddFund(Fund fund) => dbContext.Funds.Add(fund);

    public void AddOperation(FundOperation operation) => dbContext.FundOperations.Add(operation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private bool IsSqliteProvider() =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record PoolEvent(DateTimeOffset CreatedAtUtc, Guid Id, decimal Amount);

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
