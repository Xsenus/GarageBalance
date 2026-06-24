namespace GarageBalance.Api.Application.Import;

public interface IImportQuarantineService
{
    Task<IReadOnlyList<AccessImportQuarantineItemDto>> GetOpenItemsAsync(Guid? accessImportRunId, CancellationToken cancellationToken);

    Task<ImportResult<AccessImportQuarantineItemDto>> RegisterAsync(
        RegisterImportQuarantineItemRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<ImportResult<AccessImportQuarantineItemDto>> ResolveAsync(
        Guid id,
        ResolveImportQuarantineItemRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
