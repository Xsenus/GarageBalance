using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfChargeServiceSettingRepository(GarageBalanceDbContext dbContext) : IChargeServiceSettingRepository
{
    public async Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChargeServiceSettings.AsNoTracking().Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(CancellationToken cancellationToken) =>
        await dbContext.ChargeServiceSettings.AsNoTracking()
            .Where(setting => !setting.IsArchived && setting.IsRegular)
            .OrderBy(setting => setting.Name)
            .ToListAsync(cancellationToken);

    public Task<ChargeServiceSetting?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<ChargeServiceSetting?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public void Add(ChargeServiceSetting setting) => dbContext.ChargeServiceSettings.Add(setting);
}
