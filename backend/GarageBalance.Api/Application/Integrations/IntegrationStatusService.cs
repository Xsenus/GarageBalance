namespace GarageBalance.Api.Application.Integrations;

public sealed class IntegrationStatusService(IIntegrationSecretSettingsService secretSettingsService) : IIntegrationStatusService
{
    private const string OneCFreshProvider = "OneCFresh";
    private const string RefreshTokenSettingKey = "RefreshToken";
    private static readonly string[] OneCFreshRequiredSettings = [RefreshTokenSettingKey];

    public async Task<OneCFreshIntegrationStatusDto> GetOneCFreshStatusAsync(CancellationToken cancellationToken)
    {
        var settings = await secretSettingsService.GetSettingsAsync(OneCFreshProvider, cancellationToken);
        var configuredSettings = settings
            .Where(setting => setting.HasProtectedValue)
            .Select(setting => setting.SettingKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hasRefreshToken = configuredSettings.Contains(RefreshTokenSettingKey, StringComparer.OrdinalIgnoreCase);
        var lastProtectedSettingUpdatedAtUtc = settings
            .Where(setting => setting.HasProtectedValue)
            .Select(setting => (DateTimeOffset?)setting.UpdatedAtUtc)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return new OneCFreshIntegrationStatusDto(
            OneCFreshProvider,
            "1C Fresh",
            hasRefreshToken,
            false,
            hasRefreshToken ? "prepared" : "not_configured",
            hasRefreshToken
                ? "Токен 1C Fresh сохранен в защищенном хранилище. Запуск синхронизации будет доступен после подключения адаптера 1C Fresh."
                : "Для будущей синхронизации нужно сохранить защищенную настройку OneCFresh:RefreshToken.",
            OneCFreshRequiredSettings,
            configuredSettings,
            lastProtectedSettingUpdatedAtUtc);
    }
}
