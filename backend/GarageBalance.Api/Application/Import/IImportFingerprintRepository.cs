using GarageBalance.Api.Domain.Import;

namespace GarageBalance.Api.Application.Import;

public interface IImportFingerprintRepository
{
    Task<AccessImportRowFingerprint?> FindByKeyAsync(string fingerprintKey, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, AccessImportRowFingerprint>> FindByKeysAsync(
        IReadOnlyCollection<string> fingerprintKeys,
        CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string fingerprintKey, CancellationToken cancellationToken);
    void Add(AccessImportRowFingerprint fingerprint);
    void AddRange(IEnumerable<AccessImportRowFingerprint> fingerprints);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
