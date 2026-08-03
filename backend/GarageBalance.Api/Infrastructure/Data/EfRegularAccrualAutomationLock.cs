using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using GarageBalance.Api.Application.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfRegularAccrualAutomationLock(GarageBalanceDbContext dbContext)
    : IRegularAccrualAutomationLock
{
    private const long LockNamespace = 0x47424143L;
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> LocalLocks = new();

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var month = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        var lockKey = CreateLockKey(month);
        if (!dbContext.Database.IsNpgsql())
        {
            var semaphore = LocalLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken)
                ? new LocalLockLease(semaphore)
                : null;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var acquired = await ExecuteLockCommandAsync(
                connection,
                "SELECT pg_try_advisory_lock(@lock_key)",
                lockKey,
                cancellationToken);
            if (!acquired)
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }

                return null;
            }

            return new PostgreSqlLockLease(connection, lockKey, closeConnection);
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

    internal static long CreateLockKey(DateOnly accountingMonth) =>
        unchecked((LockNamespace << 32) | (uint)(accountingMonth.Year * 100 + accountingMonth.Month));

    private static async Task<bool> ExecuteLockCommandAsync(
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
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private sealed class LocalLockLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PostgreSqlLockLease(
        DbConnection connection,
        long lockKey,
        bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteLockCommandAsync(
                    connection,
                    "SELECT pg_advisory_unlock(@lock_key)",
                    lockKey,
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
}
