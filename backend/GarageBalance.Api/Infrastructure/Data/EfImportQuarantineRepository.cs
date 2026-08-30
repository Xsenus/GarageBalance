using GarageBalance.Api.Application.Import;
using GarageBalance.Api.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfImportQuarantineRepository(GarageBalanceDbContext dbContext) : IImportQuarantineRepository
{
    public async Task<IReadOnlyList<AccessImportQuarantineListItemData>> GetOpenItemsAsync(
        Guid? accessImportRunId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportQuarantineItems
            .AsNoTracking()
            .Where(item => item.Status == "open");

        if (accessImportRunId.HasValue)
        {
            query = query.Where(item => item.AccessImportRunId == accessImportRunId.Value);
        }

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return (await query
                    .Select(item => new AccessImportQuarantineListItemData(
                        item.Id,
                        item.AccessImportRunId,
                        item.SourceSystem,
                        item.EntityType,
                        item.ExternalId,
                        item.RowHash,
                        item.ReasonCode,
                        item.ReasonMessage,
                        item.Severity,
                        item.Status,
                        item.CreatedAtUtc,
                        item.CreatedByUserId,
                        item.ResolvedAtUtc,
                        item.ResolvedByUserId,
                        item.ResolutionComment))
                    .ToListAsync(cancellationToken))
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .Take(limit)
                .ToList();
        }

        return await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(limit)
            .Select(item => new AccessImportQuarantineListItemData(
                item.Id,
                item.AccessImportRunId,
                item.SourceSystem,
                item.EntityType,
                item.ExternalId,
                item.RowHash,
                item.ReasonCode,
                item.ReasonMessage,
                item.Severity,
                item.Status,
                item.CreatedAtUtc,
                item.CreatedByUserId,
                item.ResolvedAtUtc,
                item.ResolvedByUserId,
                item.ResolutionComment))
            .ToListAsync(cancellationToken);
    }

    public Task<AccessImportQuarantineItem?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.AccessImportQuarantineItems
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public void Add(AccessImportQuarantineItem item)
    {
        dbContext.AccessImportQuarantineItems.Add(item);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
