namespace GarageBalance.Api.Application.Integrations;

public interface IIntegrationSecretSettingsService
{
    Task<IntegrationSecretSettingResult<IntegrationSecretSettingDto>> UpsertSecretAsync(
        UpsertIntegrationSecretRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<IntegrationSecretSettingResult<string>> GetSecretAsync(
        string provider,
        string settingKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationSecretSettingDto>> GetSettingsAsync(
        string? provider,
        CancellationToken cancellationToken);
}
