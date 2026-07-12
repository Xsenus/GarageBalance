using GarageBalance.Api.Application.Integrations;
using GarageBalance.Api.Domain.Integrations;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfIntegrationSecretSettingsRepository(GarageBalanceDbContext dbContext) : IIntegrationSecretSettingsRepository
{
    public Task<IntegrationSecretSetting?> FindForUpdateAsync(
        string normalizedProvider,
        string normalizedSettingKey,
        CancellationToken cancellationToken)
    {
        return dbContext.IntegrationSecretSettings.FirstOrDefaultAsync(item =>
            item.NormalizedProvider == normalizedProvider &&
            item.NormalizedSettingKey == normalizedSettingKey,
            cancellationToken);
    }

    public Task<IntegrationSecretSetting?> FindAsync(
        string normalizedProvider,
        string normalizedSettingKey,
        CancellationToken cancellationToken)
    {
        return dbContext.IntegrationSecretSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.NormalizedProvider == normalizedProvider &&
                item.NormalizedSettingKey == normalizedSettingKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<IntegrationSecretSetting>> GetSettingsAsync(
        string? normalizedProvider,
        CancellationToken cancellationToken)
    {
        var query = dbContext.IntegrationSecretSettings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(normalizedProvider))
        {
            query = query.Where(item => item.NormalizedProvider == normalizedProvider);
        }

        return await query
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.SettingKey)
            .ToListAsync(cancellationToken);
    }

    public void Add(IntegrationSecretSetting setting)
    {
        dbContext.IntegrationSecretSettings.Add(setting);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
