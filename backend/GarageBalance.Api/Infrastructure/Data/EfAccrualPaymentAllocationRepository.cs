using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Security.Cryptography;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAccrualPaymentAllocationRepository(GarageBalanceDbContext dbContext)
    : IAccrualPaymentAllocationRepository
{
    private const int AccrualRowKind = 1;
    private const int PaymentRowKind = 2;
    private const int AllocationRowKind = 3;
    private static readonly byte[] GarageIncomeWorksheetLockNamespace =
        "garage-income-worksheet"u8.ToArray();

    public Task<IAsyncDisposable> AcquireGarageIncomeWorksheetLockAsync(
        Guid garageId,
        CancellationToken cancellationToken) =>
        AcquireAdvisoryLocksAsync([CreateGarageIncomeWorksheetLockKey(garageId)], cancellationToken);

    public async Task<IAsyncDisposable> AcquireRebuildLockAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken)
    {
        var lockKeys = keys
            .Distinct()
            .Select(CreateAdvisoryLockKey)
            .Order()
            .ToArray();
        return await AcquireAdvisoryLocksAsync(lockKeys, cancellationToken);
    }

    private async Task<IAsyncDisposable> AcquireAdvisoryLocksAsync(
        IReadOnlyList<long> lockKeys,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoOpAsyncDisposable.Instance;
        }

        if (lockKeys.Count == 0)
        {
            return NoOpAsyncDisposable.Instance;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var acquiredKeys = new List<long>(lockKeys.Count);
        try
        {
            foreach (var lockKey in lockKeys)
            {
                await ExecuteAdvisoryLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_lock(@lock_key)",
                    lockKey,
                    cancellationToken);
                acquiredKeys.Add(lockKey);
            }

            return new PostgreSqlAdvisoryLockLease(connection, acquiredKeys, closeConnection);
        }
        catch
        {
            await ReleaseLocksAsync(connection, acquiredKeys, closeConnection);
            throw;
        }
    }

    public async Task<AccrualPaymentAllocationRebuildResult> RebuildAsync(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken)
    {
        var distinctKeys = keys.Distinct().ToArray();
        if (distinctKeys.Length == 0)
        {
            return new AccrualPaymentAllocationRebuildResult(0, 0, 0);
        }

        var garageIds = distinctKeys.Select(key => key.GarageId).Distinct().ToArray();
        var incomeTypeIds = distinctKeys.Select(key => key.IncomeTypeId).Distinct().ToArray();
        var keySet = distinctKeys.ToHashSet();
        var rows = await BuildLedgerQuery(distinctKeys).ToListAsync(cancellationToken);

        OverlayTrackedAccruals(rows, garageIds, incomeTypeIds, keySet);
        await OverlayCampaignPaymentsForNormalizedTrackedAccrualsAsync(rows, keySet, cancellationToken);
        OverlayTrackedPayments(rows, garageIds, incomeTypeIds, keySet);
        var previousActiveAllocationCount = rows.Count(row =>
            row.Kind == AllocationRowKind && keySet.Contains(row.Key));
        var activeAllocationCount = 0;

        foreach (var row in rows.Where(row => row.Kind == AllocationRowKind && keySet.Contains(row.Key)))
        {
            var allocation = dbContext.AccrualPaymentAllocations.Local.FirstOrDefault(item => item.Id == row.Id);
            if (allocation is null)
            {
                allocation = new AccrualPaymentAllocation { Id = row.Id, IsActive = true };
                dbContext.AccrualPaymentAllocations.Attach(allocation);
            }

            allocation.IsActive = false;
        }

        foreach (var key in distinctKeys)
        {
            var plan = AccrualPaymentAllocator.Allocate(
                rows
                    .Where(row => row.Kind == AccrualRowKind && row.Key == key && !row.IsCanceled)
                    .Select(row => new AccrualPaymentAllocationAccrual(
                        row.Id, row.SortDate, row.AccountingMonth, row.Amount, row.CreatedAtUtc, row.FeeCampaignId, row.IrregularPaymentId)),
                rows
                    .Where(row =>
                        row.Kind == PaymentRowKind &&
                        row.Key == key &&
                        !row.IsCanceled &&
                        row.OperationKind == FinancialOperationKinds.Income)
                    .Select(row => new AccrualPaymentAllocationPayment(
                        row.Id, row.SortDate, row.AccountingMonth, row.Amount, row.CreatedAtUtc, row.FeeCampaignId, row.IrregularPaymentId)));

            dbContext.AccrualPaymentAllocations.AddRange(plan.Select(item => new AccrualPaymentAllocation
            {
                FinancialOperationId = item.FinancialOperationId,
                AccrualId = item.AccrualId,
                Amount = item.Amount
            }));
            activeAllocationCount += plan.Count;
        }

        return new AccrualPaymentAllocationRebuildResult(
            distinctKeys.Length,
            previousActiveAllocationCount,
            activeAllocationCount);
    }

    public Task<bool> HasActiveAllocationAsync(
        IReadOnlyCollection<Guid> accrualIds,
        CancellationToken cancellationToken)
    {
        if (accrualIds.Count == 0)
        {
            return Task.FromResult(false);
        }

        return dbContext.AccrualPaymentAllocations.AsNoTracking().AnyAsync(
            allocation =>
                allocation.IsActive &&
                accrualIds.Contains(allocation.AccrualId) &&
                !allocation.FinancialOperation.IsCanceled,
            cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetActivelyAllocatedAccrualIdsAsync(
        IReadOnlyCollection<Guid> accrualIds,
        CancellationToken cancellationToken)
    {
        if (accrualIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        return await dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                accrualIds.Contains(allocation.AccrualId) &&
                !allocation.FinancialOperation.IsCanceled)
            .Select(allocation => allocation.AccrualId)
            .ToHashSetAsync(cancellationToken);
    }

    private IQueryable<AllocationLedgerRow> BuildLedgerQuery(IReadOnlyCollection<AccrualPaymentAllocationKey> keys)
    {
        var garageIds = keys.Select(key => key.GarageId).Distinct().ToArray();
        var incomeTypeIds = keys.Select(key => key.IncomeTypeId).Distinct().ToArray();
        var accrualRows = dbContext.Accruals.AsNoTracking()
            .Where(item => !item.DueDateNeedsReview && garageIds.Contains(item.GarageId) && incomeTypeIds.Contains(item.IncomeTypeId))
            .Where(BuildExactKeyPredicate<Accrual>(
                keys,
                item => item.GarageId,
                item => item.IncomeTypeId))
            .Select(item => new
            {
                Kind = AccrualRowKind,
                item.Id,
                item.GarageId,
                item.IncomeTypeId,
                SortDate = item.DueDate,
                item.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                item.IsCanceled,
                OperationKind = string.Empty,
                item.FeeCampaignId,
                item.IrregularPaymentId
            });
        var untaggedPaymentRows = dbContext.FinancialOperations.AsNoTracking()
            .Where(item =>
                !item.IsCanceled &&
                item.OperationKind == FinancialOperationKinds.Income &&
                item.FeeCampaignId == null &&
                item.GarageId.HasValue && garageIds.Contains(item.GarageId.Value) &&
                item.IncomeTypeId.HasValue && incomeTypeIds.Contains(item.IncomeTypeId.Value))
            .Where(BuildExactKeyPredicate<FinancialOperation>(
                keys,
                item => item.GarageId!.Value,
                item => item.IncomeTypeId!.Value))
            .Select(item => new
            {
                Kind = PaymentRowKind,
                item.Id,
                GarageId = item.GarageId!.Value,
                IncomeTypeId = item.IncomeTypeId!.Value,
                SortDate = item.OperationDate,
                item.AccountingMonth,
                Amount = item.Amount -
                    (dbContext.AccrualPaymentAllocations
                        .Where(allocation =>
                            allocation.IsActive &&
                            allocation.FinancialOperationId == item.Id &&
                            !allocation.Accrual.IsCanceled &&
                            allocation.Accrual.FeeCampaignId.HasValue)
                        .Sum(allocation => (decimal?)allocation.Amount) ?? 0m),
                item.CreatedAtUtc,
                item.IsCanceled,
                item.OperationKind,
                item.FeeCampaignId,
                item.IrregularPaymentId
            });
        var campaignAccrualRoutes = dbContext.Accruals.AsNoTracking()
            .Where(item =>
                !item.IsCanceled &&
                !item.DueDateNeedsReview &&
                item.FeeCampaignId.HasValue &&
                garageIds.Contains(item.GarageId) &&
                incomeTypeIds.Contains(item.IncomeTypeId))
            .Where(BuildExactKeyPredicate<Accrual>(
                keys,
                item => item.GarageId,
                item => item.IncomeTypeId))
            .Select(item => new
            {
                item.GarageId,
                item.IncomeTypeId,
                FeeCampaignId = item.FeeCampaignId!.Value
            })
            .Distinct();
        var campaignPaymentRows =
            from item in dbContext.FinancialOperations.AsNoTracking()
            where !item.IsCanceled &&
                item.OperationKind == FinancialOperationKinds.Income &&
                item.FeeCampaignId.HasValue &&
                item.GarageId.HasValue
            join route in campaignAccrualRoutes
                on new
                {
                    GarageId = item.GarageId!.Value,
                    FeeCampaignId = item.FeeCampaignId!.Value
                }
                equals new
                {
                    route.GarageId,
                    route.FeeCampaignId
                }
            select new
            {
                Kind = PaymentRowKind,
                item.Id,
                route.GarageId,
                route.IncomeTypeId,
                SortDate = item.OperationDate,
                item.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                item.IsCanceled,
                item.OperationKind,
                item.FeeCampaignId,
                item.IrregularPaymentId
            };
        var migratedCampaignPaymentRows = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(allocation =>
                allocation.IsActive &&
                !allocation.Accrual.IsCanceled &&
                !allocation.Accrual.DueDateNeedsReview &&
                allocation.Accrual.FeeCampaignId.HasValue &&
                allocation.FinancialOperation.FeeCampaignId == null &&
                !allocation.FinancialOperation.IsCanceled &&
                allocation.FinancialOperation.OperationKind == FinancialOperationKinds.Income &&
                allocation.FinancialOperation.IncomeTypeId.HasValue &&
                garageIds.Contains(allocation.Accrual.GarageId) &&
                incomeTypeIds.Contains(allocation.Accrual.IncomeTypeId))
            .Where(BuildExactKeyPredicate<AccrualPaymentAllocation>(
                keys,
                allocation => allocation.Accrual.GarageId,
                allocation => allocation.Accrual.IncomeTypeId))
            .GroupBy(allocation => new
            {
                allocation.FinancialOperation.Id,
                allocation.Accrual.GarageId,
                allocation.Accrual.IncomeTypeId,
                allocation.FinancialOperation.OperationDate,
                allocation.FinancialOperation.AccountingMonth,
                allocation.FinancialOperation.CreatedAtUtc,
                allocation.FinancialOperation.IsCanceled,
                allocation.FinancialOperation.OperationKind,
                allocation.Accrual.FeeCampaignId,
                allocation.FinancialOperation.IrregularPaymentId
            })
            .Select(group => new
            {
                Kind = PaymentRowKind,
                group.Key.Id,
                group.Key.GarageId,
                group.Key.IncomeTypeId,
                SortDate = group.Key.OperationDate,
                group.Key.AccountingMonth,
                Amount = group.Sum(allocation => allocation.Amount),
                group.Key.CreatedAtUtc,
                group.Key.IsCanceled,
                group.Key.OperationKind,
                group.Key.FeeCampaignId,
                group.Key.IrregularPaymentId
            });
        var paymentRows = untaggedPaymentRows
            .Concat(campaignPaymentRows)
            .Concat(migratedCampaignPaymentRows);
        var allocationRows = dbContext.AccrualPaymentAllocations.AsNoTracking()
            .Where(item =>
                item.IsActive &&
                garageIds.Contains(item.Accrual.GarageId) &&
                incomeTypeIds.Contains(item.Accrual.IncomeTypeId))
            .Where(BuildExactKeyPredicate<AccrualPaymentAllocation>(
                keys,
                item => item.Accrual.GarageId,
                item => item.Accrual.IncomeTypeId))
            .Select(item => new
            {
                Kind = AllocationRowKind,
                item.Id,
                item.Accrual.GarageId,
                item.Accrual.IncomeTypeId,
                SortDate = item.Accrual.DueDate,
                item.Accrual.AccountingMonth,
                item.Amount,
                item.CreatedAtUtc,
                IsCanceled = false,
                OperationKind = string.Empty,
                item.Accrual.FeeCampaignId,
                item.Accrual.IrregularPaymentId
            });

        var rows = accrualRows
            .Concat(paymentRows)
            .Concat(allocationRows)
            .Select(row => new AllocationLedgerRow(
                row.Kind,
                row.Id,
                row.GarageId,
                row.IncomeTypeId,
                row.SortDate,
                row.AccountingMonth,
                row.Amount,
                row.CreatedAtUtc,
                row.IsCanceled,
                row.OperationKind,
                row.FeeCampaignId,
                row.IrregularPaymentId));
        return rows;
    }

    private static Expression<Func<TEntity, bool>> BuildExactKeyPredicate<TEntity>(
        IReadOnlyCollection<AccrualPaymentAllocationKey> keys,
        Expression<Func<TEntity, Guid>> garageSelector,
        Expression<Func<TEntity, Guid>> incomeTypeSelector)
    {
        var entity = Expression.Parameter(typeof(TEntity), "item");
        var garageId = new ParameterReplacingExpressionVisitor(
            garageSelector.Parameters[0],
            entity).Visit(garageSelector.Body)!;
        var incomeTypeId = new ParameterReplacingExpressionVisitor(
            incomeTypeSelector.Parameters[0],
            entity).Visit(incomeTypeSelector.Body)!;
        Expression body = Expression.Constant(false);

        foreach (var garageGroup in keys.GroupBy(key => key.GarageId))
        {
            var garageMatches = Expression.Equal(garageId, Expression.Constant(garageGroup.Key));
            var incomeTypeIds = garageGroup.Select(key => key.IncomeTypeId).Distinct().ToArray();
            var incomeTypeMatches = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                [typeof(Guid)],
                Expression.Constant(incomeTypeIds),
                incomeTypeId);
            body = Expression.OrElse(body, Expression.AndAlso(garageMatches, incomeTypeMatches));
        }

        return Expression.Lambda<Func<TEntity, bool>>(body, entity);
    }

    private static long CreateAdvisoryLockKey(AccrualPaymentAllocationKey key)
    {
        Span<byte> source = stackalloc byte[32];
        key.GarageId.TryWriteBytes(source[..16]);
        key.IncomeTypeId.TryWriteBytes(source[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(source, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private static long CreateGarageIncomeWorksheetLockKey(Guid garageId)
    {
        Span<byte> source = stackalloc byte[16 + GarageIncomeWorksheetLockNamespace.Length];
        garageId.TryWriteBytes(source[..16]);
        GarageIncomeWorksheetLockNamespace.CopyTo(source[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(source, hash);
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    private static async Task ExecuteAdvisoryLockCommandAsync(
        DbConnection connection,
        string commandText,
        long lockKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseLocksAsync(
        DbConnection connection,
        IReadOnlyList<long> lockKeys,
        bool closeConnection)
    {
        try
        {
            for (var index = lockKeys.Count - 1; index >= 0; index--)
            {
                await ExecuteAdvisoryLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_unlock(@lock_key)",
                    lockKeys[index],
                    CancellationToken.None);
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class PostgreSqlAdvisoryLockLease(
        DbConnection connection,
        IReadOnlyList<long> lockKeys,
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
            await ReleaseLocksAsync(connection, lockKeys, closeConnection);
        }
    }

    private sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public static NoOpAsyncDisposable Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private void OverlayTrackedAccruals(
        List<AllocationLedgerRow> rows,
        Guid[] garageIds,
        Guid[] incomeTypeIds,
        IReadOnlySet<AccrualPaymentAllocationKey> keys)
    {
        foreach (var accrual in dbContext.ChangeTracker.Entries<Accrual>().Select(entry => entry.Entity))
        {
            rows.RemoveAll(row => row.Kind == AccrualRowKind && row.Id == accrual.Id);
            if (!accrual.DueDateNeedsReview &&
                garageIds.Contains(accrual.GarageId) &&
                incomeTypeIds.Contains(accrual.IncomeTypeId) &&
                keys.Contains(new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)))
            {
                rows.Add(new AllocationLedgerRow(
                    AccrualRowKind,
                    accrual.Id,
                    accrual.GarageId,
                    accrual.IncomeTypeId,
                    accrual.DueDate,
                    accrual.AccountingMonth,
                    accrual.Amount,
                    accrual.CreatedAtUtc,
                    accrual.IsCanceled,
                    string.Empty,
                    accrual.FeeCampaignId,
                    accrual.IrregularPaymentId));
            }
        }
    }

    private void OverlayTrackedPayments(
        List<AllocationLedgerRow> rows,
        Guid[] garageIds,
        Guid[] incomeTypeIds,
        IReadOnlySet<AccrualPaymentAllocationKey> keys)
    {
        foreach (var payment in dbContext.ChangeTracker.Entries<FinancialOperation>().Select(entry => entry.Entity))
        {
            rows.RemoveAll(row => row.Kind == PaymentRowKind && row.Id == payment.Id);
            var campaignRoute = payment.GarageId.HasValue && payment.FeeCampaignId.HasValue
                ? rows.FirstOrDefault(row =>
                    row.Kind == AccrualRowKind &&
                    !row.IsCanceled &&
                    row.GarageId == payment.GarageId.Value &&
                    row.FeeCampaignId == payment.FeeCampaignId.Value)
                : null;
            var routedIncomeTypeId = campaignRoute?.IncomeTypeId ?? payment.IncomeTypeId;
            if (payment.GarageId.HasValue && routedIncomeTypeId.HasValue &&
                garageIds.Contains(payment.GarageId.Value) && incomeTypeIds.Contains(routedIncomeTypeId.Value) &&
                keys.Contains(new AccrualPaymentAllocationKey(payment.GarageId.Value, routedIncomeTypeId.Value)))
            {
                rows.Add(new AllocationLedgerRow(
                    PaymentRowKind,
                    payment.Id,
                    payment.GarageId.Value,
                    routedIncomeTypeId.Value,
                    payment.OperationDate,
                    payment.AccountingMonth,
                    payment.Amount,
                    payment.CreatedAtUtc,
                    payment.IsCanceled,
                    payment.OperationKind,
                    payment.FeeCampaignId,
                    payment.IrregularPaymentId));
            }
        }
    }

    private async Task OverlayCampaignPaymentsForNormalizedTrackedAccrualsAsync(
        List<AllocationLedgerRow> rows,
        IReadOnlySet<AccrualPaymentAllocationKey> keys,
        CancellationToken cancellationToken)
    {
        var routes = dbContext.ChangeTracker.Entries<Accrual>()
            .Where(entry =>
                entry.State == EntityState.Modified &&
                entry.Property(nameof(Accrual.IncomeTypeId)).IsModified)
            .Select(entry => entry.Entity)
            .Where(accrual =>
                !accrual.IsCanceled &&
                !accrual.DueDateNeedsReview &&
                accrual.FeeCampaignId.HasValue &&
                keys.Contains(new AccrualPaymentAllocationKey(accrual.GarageId, accrual.IncomeTypeId)))
            .GroupBy(accrual => (accrual.GarageId, FeeCampaignId: accrual.FeeCampaignId!.Value))
            .ToDictionary(group => group.Key, group => group.First().IncomeTypeId);
        if (routes.Count == 0)
        {
            return;
        }

        var garageIds = routes.Keys.Select(key => key.GarageId).Distinct().ToArray();
        var campaignIds = routes.Keys.Select(key => key.FeeCampaignId).Distinct().ToArray();
        var payments = await dbContext.FinancialOperations.AsNoTracking()
            .Where(payment =>
                !payment.IsCanceled &&
                payment.OperationKind == FinancialOperationKinds.Income &&
                payment.GarageId.HasValue &&
                payment.FeeCampaignId.HasValue &&
                garageIds.Contains(payment.GarageId.Value) &&
                campaignIds.Contains(payment.FeeCampaignId.Value))
            .Select(payment => new
            {
                payment.Id,
                GarageId = payment.GarageId!.Value,
                FeeCampaignId = payment.FeeCampaignId!.Value,
                payment.OperationDate,
                payment.AccountingMonth,
                payment.Amount,
                payment.CreatedAtUtc,
                payment.IsCanceled,
                payment.OperationKind,
                payment.IrregularPaymentId
            })
            .ToListAsync(cancellationToken);
        foreach (var payment in payments)
        {
            if (!routes.TryGetValue((payment.GarageId, payment.FeeCampaignId), out var routedIncomeTypeId))
            {
                continue;
            }

            rows.RemoveAll(row => row.Kind == PaymentRowKind && row.Id == payment.Id);
            rows.Add(new AllocationLedgerRow(
                PaymentRowKind,
                payment.Id,
                payment.GarageId,
                routedIncomeTypeId,
                payment.OperationDate,
                payment.AccountingMonth,
                payment.Amount,
                payment.CreatedAtUtc,
                payment.IsCanceled,
                payment.OperationKind,
                payment.FeeCampaignId,
                payment.IrregularPaymentId));
        }
    }

    private sealed record AllocationLedgerRow(
        int Kind,
        Guid Id,
        Guid GarageId,
        Guid IncomeTypeId,
        DateOnly SortDate,
        DateOnly AccountingMonth,
        decimal Amount,
        DateTimeOffset CreatedAtUtc,
        bool IsCanceled,
        string OperationKind,
        Guid? FeeCampaignId,
        Guid? IrregularPaymentId)
    {
        public AccrualPaymentAllocationKey Key => new(GarageId, IncomeTypeId);
    }

    private sealed class ParameterReplacingExpressionVisitor(
        ParameterExpression source,
        ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}
