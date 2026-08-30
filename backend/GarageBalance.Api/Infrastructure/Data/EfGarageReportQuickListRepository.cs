using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageReportQuickListRepository(GarageBalanceDbContext dbContext) : IGarageReportQuickListRepository
{
    public async Task<IReadOnlyList<GarageReportQuickList>> GetAllAsync(CancellationToken cancellationToken)
    {
        var quickLists = dbContext.GarageReportQuickLists
            .AsNoTracking()
            .Where(quickList => !quickList.IsArchived)
            .OrderBy(quickList => quickList.NormalizedName)
            .ThenBy(quickList => quickList.Id)
            .Take(100);
        var rows = await (
                from quickList in quickLists
                join membership in dbContext.GarageReportQuickListGarages.AsNoTracking()
                    on quickList.Id equals membership.QuickListId into memberships
                from membership in memberships.DefaultIfEmpty()
                join garage in dbContext.Garages.AsNoTracking()
                    on (membership == null ? null : (Guid?)membership.GarageId) equals (Guid?)garage.Id into garages
                from garage in garages.DefaultIfEmpty()
                join owner in dbContext.Owners.AsNoTracking()
                    on garage.OwnerId equals (Guid?)owner.Id into owners
                from owner in owners.DefaultIfEmpty()
                select new GarageReportQuickListRow
                {
                    QuickListId = quickList.Id,
                    Name = quickList.Name,
                    NormalizedName = quickList.NormalizedName,
                    UpdatedAtUtc = quickList.UpdatedAtUtc,
                    UpdatedByUserId = quickList.UpdatedByUserId,
                    GarageId = garage == null ? null : garage.Id,
                    GarageNumber = garage == null ? null : garage.Number,
                    GarageIsArchived = garage == null ? null : garage.IsArchived,
                    OwnerId = owner == null ? null : owner.Id,
                    OwnerLastName = owner == null ? null : owner.LastName,
                    OwnerFirstName = owner == null ? null : owner.FirstName,
                    OwnerMiddleName = owner == null ? null : owner.MiddleName
                })
            .OrderBy(row => row.NormalizedName)
            .ThenBy(row => row.QuickListId)
            .ThenBy(row => row.GarageNumber)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => new
            {
                row.QuickListId,
                row.Name,
                row.NormalizedName,
                row.UpdatedAtUtc,
                row.UpdatedByUserId
            })
            .Select(group => new GarageReportQuickList
            {
                Id = group.Key.QuickListId,
                Name = group.Key.Name,
                NormalizedName = group.Key.NormalizedName,
                UpdatedAtUtc = group.Key.UpdatedAtUtc,
                UpdatedByUserId = group.Key.UpdatedByUserId,
                Garages = group
                    .Where(row => row.GarageId.HasValue)
                    .Select(row => new GarageReportQuickListGarage
                    {
                        QuickListId = group.Key.QuickListId,
                        GarageId = row.GarageId!.Value,
                        Garage = new Garage
                        {
                            Id = row.GarageId.Value,
                            Number = row.GarageNumber!,
                            IsArchived = row.GarageIsArchived!.Value,
                            OwnerId = row.OwnerId,
                            Owner = row.OwnerId.HasValue
                                ? new Owner
                                {
                                    Id = row.OwnerId.Value,
                                    LastName = row.OwnerLastName!,
                                    FirstName = row.OwnerFirstName!,
                                    MiddleName = row.OwnerMiddleName
                                }
                                : null
                        }
                    })
                    .ToList()
            })
            .ToList();
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

    private sealed class GarageReportQuickListRow
    {
        public Guid QuickListId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string NormalizedName { get; init; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public Guid? UpdatedByUserId { get; init; }
        public Guid? GarageId { get; init; }
        public string? GarageNumber { get; init; }
        public bool? GarageIsArchived { get; init; }
        public Guid? OwnerId { get; init; }
        public string? OwnerLastName { get; init; }
        public string? OwnerFirstName { get; init; }
        public string? OwnerMiddleName { get; init; }
    }
}
