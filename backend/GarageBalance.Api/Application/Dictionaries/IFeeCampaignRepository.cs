using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IFeeCampaignRepository
{
    Task<IReadOnlyList<FeeCampaign>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<FeeCampaign>> GetActiveAccrualCandidatesAsync(DateOnly accountingMonth, int limit, CancellationToken cancellationToken);
    Task<FeeCampaign?> FindActiveWithDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<FeeCampaign?> FindActiveForAccrualGenerationAsync(Guid id, CancellationToken cancellationToken);
    Task<FeeCampaign?> FindArchivedWithDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<bool> HasAccrualsAsync(Guid id, CancellationToken cancellationToken);
    void Add(FeeCampaign campaign);
}
