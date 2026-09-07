using System.Data;
using GarageBalance.Api.Application.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfExpenseBatchTransactionRunner(GarageBalanceDbContext dbContext) : IExpenseBatchTransactionRunner
{
    public async Task<FinanceResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<FinanceResult<T>>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Массовая выплата должна начинаться без незавершённых изменений.");
        }
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            if (result.Succeeded)
            {
                if (dbContext.ChangeTracker.HasChanges())
                {
                    throw new InvalidOperationException("Не все изменения массовой выплаты сохранены.");
                }
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            return result;
        }
        catch (Exception exception) when (IsSerializationConflict(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return FinanceResult<T>.Failure("expense_batch_concurrency",
                "Данные изменились во время выплаты. Обновите предварительный расчёт и повторите операцию.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            // Failed SaveChanges can leave added entities and modified balances
            // tracked. They must not escape into a later save in this request.
            dbContext.ChangeTracker.Clear();
        }
    }

    private static bool IsSerializationConflict(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }
        || ((exception is DbUpdateException or InvalidOperationException)
            && exception.InnerException is { } inner && IsSerializationConflict(inner));
}
