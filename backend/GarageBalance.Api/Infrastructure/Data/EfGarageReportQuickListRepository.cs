using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageReportQuickListRepository(GarageBalanceDbContext dbContext) : IGarageReportQuickListRepository
{
    public async Task<IReadOnlyList<GarageReportQuickList>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.GarageReportQuickLists
            .AsNoTracking()
            .Where(quickList => !quickList.IsArchived)
            .Include(quickList => quickList.Garages)
                .ThenInclude(item => item.Garage)
                    .ThenInclude(garage => garage.Owner)
            .OrderBy(quickList => quickList.NormalizedName)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public Task<GarageReportQuickList?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.GarageReportQuickLists
            .Where(quickList => !quickList.IsArchived)
            .Include(quickList => quickList.Garages)
                .ThenInclude(item => item.Garage)
                    .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(quickList => quickList.Id == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string normalizedName, Guid? exceptId, CancellationToken cancellationToken)
    {
        return dbContext.GarageReportQuickLists.AnyAsync(
            quickList => !quickList.IsArchived
                && quickList.NormalizedName == normalizedName
                && (!exceptId.HasValue || quickList.Id != exceptId.Value),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Garage>> GetActiveGaragesAsync(
        IReadOnlySet<Guid> garageIds,
        CancellationToken cancellationToken)
    {
        return await dbContext.Garages
            .Where(garage => garageIds.Contains(garage.Id) && !garage.IsArchived)
            .OrderBy(garage => garage.Number)
            .ToListAsync(cancellationToken);
    }

    public void Add(GarageReportQuickList quickList)
    {
        dbContext.GarageReportQuickLists.Add(quickList);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
