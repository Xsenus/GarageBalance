namespace GarageBalance.Api.Application.Integrations;

public sealed record OneCFreshIntegrationStatusDto(
    string Provider,
    string DisplayName,
    bool IsConfigured,
    bool CanSynchronize,
    string Status,
    string StatusMessage,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> ConfiguredSettings,
    DateTimeOffset? LastProtectedSettingUpdatedAtUtc);

public sealed record ReceiptPrintingIntegrationStatusDto(
    string Provider,
    string DisplayName,
    bool IsConfigured,
    bool CanPrint,
    string Status,
    string StatusMessage,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<string> ConfiguredSettings,
    IReadOnlyList<string> PlannedActions,
    DateTimeOffset? LastProtectedSettingUpdatedAtUtc);
