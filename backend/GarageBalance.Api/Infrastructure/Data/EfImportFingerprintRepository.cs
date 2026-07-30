using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfImportFingerprintRepository(GarageBalanceDbContext dbContext) : IImportFingerprintRepository
{
    public Task<AccessImportRowFingerprint?> FindByKeyAsync(string fingerprintKey, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportRowFingerprints
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.FingerprintKey == fingerprintKey, cancellationToken);
    }

    public Task<bool> ExistsAsync(string fingerprintKey, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportRowFingerprints
            .AnyAsync(item => item.FingerprintKey == fingerprintKey, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, AccessImportRowFingerprint>> FindByKeysAsync(
        IReadOnlyCollection<string> fingerprintKeys,
        CancellationToken cancellationToken)
    {
        if (fingerprintKeys.Count == 0)
        {
            return new Dictionary<string, AccessImportRowFingerprint>(StringComparer.Ordinal);
        }

        return await dbContext.AccessImportRowFingerprints
            .AsNoTracking()
            .Where(item => fingerprintKeys.Contains(item.FingerprintKey))
            .ToDictionaryAsync(item => item.FingerprintKey, StringComparer.Ordinal, cancellationToken);
    }

    public void Add(AccessImportRowFingerprint fingerprint)
    {
        dbContext.AccessImportRowFingerprints.Add(fingerprint);
    }

    public void AddRange(IEnumerable<AccessImportRowFingerprint> fingerprints)
    {
        dbContext.AccessImportRowFingerprints.AddRange(fingerprints);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
