using GarageBalance.Api.Domain.Integrations;

namespace GarageBalance.Api.Application.Integrations;

public interface IIntegrationSecretSettingsRepository
{
    Task<IntegrationSecretSetting?> FindForUpdateAsync(
        string normalizedProvider,
        string normalizedSettingKey,
        CancellationToken cancellationToken);

    Task<IntegrationSecretSetting?> FindAsync(
        string normalizedProvider,
        string normalizedSettingKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationSecretSetting>> GetSettingsAsync(
        string? normalizedProvider,
        CancellationToken cancellationToken);

    void Add(IntegrationSecretSetting setting);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
