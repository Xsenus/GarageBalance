using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportQuarantineRepository
{
    Task<IReadOnlyList<AccessImportQuarantineItem>> GetOpenItemsAsync(
        Guid? accessImportRunId,
        int limit,
        CancellationToken cancellationToken);

    Task<AccessImportQuarantineItem?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);
    void Add(AccessImportQuarantineItem item);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
