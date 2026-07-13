namespace GarageBalance.Api.Application.Integrations;

public static class IntegrationSecretCatalog
{
    public const string OneCFreshProvider = "OneCFresh";
    public const string OneCFreshRefreshToken = "RefreshToken";
    public const string ReceiptPrintingProvider = "ReceiptPrinting";
    public const string ReceiptPrintingDeviceConnection = "DeviceConnection";
    public const string ReceiptPrintingReceiptTemplate = "ReceiptTemplate";
    public const string DadataProvider = "DaData";
    public const string DadataApiKey = "ApiKey";

    private static readonly (string Provider, string SettingKey)[] SupportedSettings =
    [
        (OneCFreshProvider, OneCFreshRefreshToken),
        (ReceiptPrintingProvider, ReceiptPrintingDeviceConnection),
        (ReceiptPrintingProvider, ReceiptPrintingReceiptTemplate),
        (DadataProvider, DadataApiKey)
    ];

    public static bool TryGetCanonical(string provider, string settingKey, out string canonicalProvider, out string canonicalSettingKey)
    {
        var match = SupportedSettings.FirstOrDefault(item =>
            string.Equals(item.Provider, provider.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.SettingKey, settingKey.Trim(), StringComparison.OrdinalIgnoreCase));
        canonicalProvider = match.Provider ?? string.Empty;
        canonicalSettingKey = match.SettingKey ?? string.Empty;
        return canonicalProvider.Length > 0;
    }
}
