using System.Data;
using System.Data.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfOpeningBalanceAdjustmentRepository(GarageBalanceDbContext dbContext) : IOpeningBalanceAdjustmentRepository
{
    public async Task<IAsyncDisposable> AcquireUpdateLockAsync(string targetKind, Guid targetId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            return NoopAsyncDisposable.Instance;
        }

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var lockKey = CreateLockKey(targetKind, targetId);
        await ExecuteLockCommandAsync(connection, "SELECT pg_advisory_lock(@lock_key)", lockKey, cancellationToken);
        return new AdvisoryLockLease(connection, lockKey, closeConnection);
    }

    public async Task<IReadOnlyList<OpeningBalanceAdjustment>> GetListAsync(string targetKind, Guid targetId, CancellationToken cancellationToken) =>
        await dbContext.OpeningBalanceAdjustments.AsNoTracking()
            .Where(item => item.TargetKind == targetKind && item.TargetId == targetId)
            .OrderByDescending(item => item.EffectiveDate)
            .ThenByDescending(item => item.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

    public void Add(OpeningBalanceAdjustment adjustment) => dbContext.OpeningBalanceAdjustments.Add(adjustment);

    private static long CreateLockKey(string targetKind, Guid targetId)
    {
        var bytes = targetId.ToByteArray();
        var key = BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
        return targetKind == OpeningBalanceAdjustmentTargetKinds.Supplier ? key ^ long.MinValue : key;
    }

    private static async Task ExecuteLockCommandAsync(DbConnection connection, string sql, long lockKey, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class AdvisoryLockLease(DbConnection connection, long lockKey, bool closeConnection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteLockCommandAsync(connection, "SELECT pg_advisory_unlock(@lock_key)", lockKey, CancellationToken.None);
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

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public static readonly NoopAsyncDisposable Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
