using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IOpeningBalanceAdjustmentRepository
{
    Task<IAsyncDisposable> AcquireUpdateLockAsync(string targetKind, Guid targetId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpeningBalanceAdjustment>> GetListAsync(string targetKind, Guid targetId, CancellationToken cancellationToken);
    void Add(OpeningBalanceAdjustment adjustment);
}
