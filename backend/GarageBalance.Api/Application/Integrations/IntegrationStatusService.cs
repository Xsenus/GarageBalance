namespace GarageBalance.Api.Application.Integrations;

public sealed class IntegrationStatusService(IIntegrationSecretSettingsService secretSettingsService) : IIntegrationStatusService
{
    private const string OneCFreshProvider = "OneCFresh";
    private const string ReceiptPrintingProvider = "ReceiptPrinting";
    private const string RefreshTokenSettingKey = "RefreshToken";
    private const string DeviceConnectionSettingKey = "DeviceConnection";
    private const string ReceiptTemplateSettingKey = "ReceiptTemplate";
    private static readonly string[] OneCFreshRequiredSettings = [RefreshTokenSettingKey];
    private static readonly string[] ReceiptPrintingRequiredSettings = [DeviceConnectionSettingKey, ReceiptTemplateSettingKey];
    private static readonly string[] ReceiptPrintingPlannedActions = ["Печать квитанции", "Отмена печати", "Печать копии квитанции"];

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

    public async Task<ReceiptPrintingIntegrationStatusDto> GetReceiptPrintingStatusAsync(CancellationToken cancellationToken)
    {
        var settings = await secretSettingsService.GetSettingsAsync(ReceiptPrintingProvider, cancellationToken);
        var configuredSettings = settings
            .Where(setting => setting.HasProtectedValue)
            .Select(setting => setting.SettingKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hasRequiredSettings = ReceiptPrintingRequiredSettings
            .All(requiredSetting => configuredSettings.Contains(requiredSetting, StringComparer.OrdinalIgnoreCase));
        var lastProtectedSettingUpdatedAtUtc = settings
            .Where(setting => setting.HasProtectedValue)
            .Select(setting => (DateTimeOffset?)setting.UpdatedAtUtc)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        return new ReceiptPrintingIntegrationStatusDto(
            ReceiptPrintingProvider,
            "Печать чеков и квитанций",
            hasRequiredSettings,
            false,
            hasRequiredSettings ? "prepared" : "not_configured",
            hasRequiredSettings
                ? "Защищенные настройки печати сохранены. Печать, отмена и повторная печать станут доступны после подключения адаптера фискального оборудования."
                : "Для будущей печати нужно сохранить защищенные настройки ReceiptPrinting:DeviceConnection и ReceiptPrinting:ReceiptTemplate.",
            ReceiptPrintingRequiredSettings,
            configuredSettings,
            ReceiptPrintingPlannedActions,
            lastProtectedSettingUpdatedAtUtc);
    }
}
