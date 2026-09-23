using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GarageBalance.Api.Application.Storage;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Storage;

/// <summary>Excludes overlapping backup creation/retention across processes without a long database transaction.</summary>
public sealed class StorageMaintenanceLock(GarageBalanceDbContext dbContext) : IStorageMaintenanceLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable?> TryAcquireAsync(string scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 256)
        {
            throw new ArgumentException("A bounded maintenance scope is required.", nameof(scope));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!dbContext.Database.IsNpgsql())
        {
            var semaphore = LocalLocks.GetOrAdd(scope, _ => new SemaphoreSlim(1, 1));
            return await semaphore.WaitAsync(0, cancellationToken) ? new LocalLease(semaphore) : null;
        }

        // A dedicated session keeps ownership stable while the request's EF connection opens/closes.
        var connectionOptions = new NpgsqlConnectionStringBuilder(dbContext.Database.GetConnectionString())
        {
            Multiplexing = false,
            Pooling = false
        };
        var connection = new NpgsqlConnection(connectionOptions.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes("GarageBalance.Storage:" + scope));
            var key = BinaryPrimitives.ReadInt64LittleEndian(hash);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new PostgreSqlLease(connection, key);
            }
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class PostgreSqlLease(NpgsqlConnection connection, long key) : IAsyncDisposable
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
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    private sealed class LocalLease(SemaphoreSlim semaphore) : IAsyncDisposable
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
