using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfFeeCampaignRepository(GarageBalanceDbContext dbContext) : IFeeCampaignRepository
{
    public async Task<IReadOnlyList<FeeCampaign>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = WithDetails(dbContext.FeeCampaigns.AsNoTracking())
            .Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item =>
                item.Name.ToLower().Contains(normalizedSearch) ||
                (item.Goal != null && item.Goal.ToLower().Contains(normalizedSearch)));
        }

        return await query
            .OrderBy(item => item.IsArchived)
            .ThenByDescending(item => item.StartsOn)
            .ThenBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<FeeCampaign?> FindActiveWithDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        WithDetails(dbContext.FeeCampaigns)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<FeeCampaign?> FindActiveForAccrualGenerationAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.FeeCampaigns
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage)
                    .ThenInclude(garage => garage.Owner)
            .SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<FeeCampaign?> FindArchivedWithDetailsAsync(Guid id, CancellationToken cancellationToken) =>
        WithDetails(dbContext.FeeCampaigns)
            .SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.FeeCampaigns.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<bool> HasAccrualsAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Accruals.AsNoTracking().AnyAsync(item => item.FeeCampaignId == id, cancellationToken);

    public void Add(FeeCampaign campaign) => dbContext.FeeCampaigns.Add(campaign);

    private static IQueryable<FeeCampaign> WithDetails(IQueryable<FeeCampaign> query) =>
        query
            .Include(item => item.IncomeType)
            .Include(item => item.ParticipantGarages)
                .ThenInclude(item => item.Garage);
}
