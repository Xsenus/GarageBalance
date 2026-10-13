using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Finance;

public interface IGarageAccountingStartService
{
    Task<FinanceResult<GarageAccountingStartChange>> BeginAsync(
        Garage garage, DateOnly month, Guid? expectedVersion, bool isNew,
        Guid? actorUserId, CancellationToken cancellationToken);
}

/// <summary>Holds the write transaction and financial locks until the entire card is saved.</summary>
public sealed class GarageAccountingStartChange(
    IMeterConnectionWriteScope transaction, IReadOnlyList<IAsyncDisposable> locks,
    Func<CancellationToken, Task<FinanceResult<bool>>> complete) : IAsyncDisposable
{
    public async Task<FinanceResult<bool>> CompleteAsync(CancellationToken cancellationToken)
    {
        var result = await complete(cancellationToken);
        if (result.Succeeded) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var heldLock in locks.Reverse()) await heldLock.DisposeAsync();
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
}
