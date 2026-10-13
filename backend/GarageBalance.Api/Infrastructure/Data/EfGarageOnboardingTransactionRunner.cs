using System.Data;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Application.Finance;
using GarageBalance.Api.Application.Funds;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageOnboardingTransactionRunner(GarageBalanceDbContext dbContext,
    IFundRepository? fundRepository = null, IAccrualPaymentAllocationRepository? allocationRepository = null) : IGarageOnboardingTransactionRunner
{
    public async Task<DictionaryResult<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<DictionaryResult<T>>> action,
        CancellationToken cancellationToken,
        Guid? garageId = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Сохранение гаража должно начинаться без незавершённых изменений.");
        }

        // Preserve the common funds -> worksheet -> transaction order until the entire
        // combined card/payment operation commits, including inner accounting savepoints.
        await using var fundLock = await (fundRepository ?? new EfFundRepository(dbContext)).AcquireAllocationLockAsync(cancellationToken);
        await using var garageLock = garageId.HasValue
            ? await (allocationRepository ?? new EfAccrualPaymentAllocationRepository(dbContext)).AcquireGarageIncomeWorksheetLockAsync(garageId.Value, cancellationToken)
            : null;
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            if (result.Succeeded)
            {
                if (dbContext.ChangeTracker.HasChanges())
                {
                    throw new InvalidOperationException("Не все изменения карточки гаража сохранены.");
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
            return DictionaryResult<T>.Failure(
                "garage_onboarding_concurrency",
                "Данные изменились во время сохранения гаража. Обновите форму и повторите операцию.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private static bool IsSerializationConflict(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }
        || ((exception is DbUpdateException or InvalidOperationException)
            && exception.InnerException is { } inner && IsSerializationConflict(inner));
}
