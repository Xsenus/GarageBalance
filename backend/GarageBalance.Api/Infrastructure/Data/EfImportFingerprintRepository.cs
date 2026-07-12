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

    public void Add(AccessImportRowFingerprint fingerprint)
    {
        dbContext.AccessImportRowFingerprints.Add(fingerprint);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
