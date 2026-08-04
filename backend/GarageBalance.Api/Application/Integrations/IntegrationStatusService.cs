namespace GarageBalance.Api.Application.Integrations;

public sealed class IntegrationStatusService(
    IIntegrationSecretSettingsService secretSettingsService,
    IOneCFreshSyncAdapter? oneCFreshSyncAdapter = null,
    IReceiptPrintingAdapter? receiptPrintingAdapter = null) : IIntegrationStatusService
{
    private const string OneCFreshProvider = IntegrationSecretCatalog.OneCFreshProvider;
    private const string ReceiptPrintingProvider = IntegrationSecretCatalog.ReceiptPrintingProvider;
    private const string RefreshTokenSettingKey = IntegrationSecretCatalog.OneCFreshRefreshToken;
    private const string DeviceConnectionSettingKey = IntegrationSecretCatalog.ReceiptPrintingDeviceConnection;
    private const string ReceiptTemplateSettingKey = IntegrationSecretCatalog.ReceiptPrintingReceiptTemplate;
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

        var adapterAvailability = oneCFreshSyncAdapter?.Availability ??
                                  new IntegrationAdapterAvailability(false, "prepared", "Токен сохранен; адаптер 1C Fresh ожидает подключения.");
        var isAvailable = hasRefreshToken && adapterAvailability.IsAvailable;

        return new OneCFreshIntegrationStatusDto(
            OneCFreshProvider,
            "1C Fresh",
            hasRefreshToken,
            isAvailable,
            !hasRefreshToken ? "not_configured" : adapterAvailability.Status,
            !hasRefreshToken
                ? "Для синхронизации нужно сохранить защищенную настройку OneCFresh:RefreshToken."
                : adapterAvailability.Message,
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

        var adapterAvailability = receiptPrintingAdapter?.Availability ??
                                  new IntegrationAdapterAvailability(false, "prepared", "Настройки сохранены; адаптер печати ожидает подключения.");
        var isAvailable = hasRequiredSettings && adapterAvailability.IsAvailable;

        return new ReceiptPrintingIntegrationStatusDto(
            ReceiptPrintingProvider,
            "Печать чеков и квитанций",
            hasRequiredSettings,
            isAvailable,
            !hasRequiredSettings ? "not_configured" : adapterAvailability.Status,
            !hasRequiredSettings
                ? "Для печати нужно сохранить защищенные настройки ReceiptPrinting:DeviceConnection и ReceiptPrinting:ReceiptTemplate."
                : adapterAvailability.Message,
            ReceiptPrintingRequiredSettings,
            configuredSettings,
            ReceiptPrintingPlannedActions,
            lastProtectedSettingUpdatedAtUtc);
    }
}
