using System.Data;
using System.Data.Common;
using GarageBalance.Api.Application.Auth;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Security;

public sealed class UserSecurityMutationLock(GarageBalanceDbContext dbContext) : IUserSecurityMutationLock
{
    private const long AdvisoryLockKey = 0x474255534543;
    private static readonly SemaphoreSlim ProcessLock = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            await ProcessLock.WaitAsync(cancellationToken);
            return new SemaphoreLease(ProcessLock);
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State == ConnectionState.Closed;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await ExecuteLockCommandAsync(connection, "SELECT pg_advisory_lock(@lock_key)", cancellationToken);
            return new PostgreSqlLease(connection, closeConnection);
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

    private static async Task ExecuteLockCommandAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = AdvisoryLockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class PostgreSqlLease(DbConnection connection, bool closeConnection) : IAsyncDisposable
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
                await ExecuteLockCommandAsync(connection, "SELECT pg_advisory_unlock(@lock_key)", CancellationToken.None);
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

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private bool disposed;

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
