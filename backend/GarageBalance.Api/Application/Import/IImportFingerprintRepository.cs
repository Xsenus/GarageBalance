using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportFingerprintRepository
{
    Task<AccessImportRowFingerprint?> FindByKeyAsync(string fingerprintKey, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string fingerprintKey, CancellationToken cancellationToken);
    void Add(AccessImportRowFingerprint fingerprint);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
