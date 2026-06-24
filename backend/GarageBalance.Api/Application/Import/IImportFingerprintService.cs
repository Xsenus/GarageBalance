namespace GarageBalance.Api.Application.Import;

public interface IImportFingerprintService
{
    Task<ImportResult<RegisterImportRowFingerprintDto>> RegisterAsync(
        RegisterImportRowFingerprintRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        string sourceSystem,
        string entityType,
        string? externalId,
        string rowHash,
        CancellationToken cancellationToken);
}
