namespace GarageBalance.Api.Application.Integrations;

public sealed record UpsertIntegrationSecretRequest(string Provider, string SettingKey, string PlaintextValue);

public sealed record UpdateIntegrationSecretRequest(string PlaintextValue);

public sealed record IntegrationSecretSettingDto(
    Guid Id,
    string Provider,
    string SettingKey,
    string Purpose,
    DateTimeOffset UpdatedAtUtc,
    Guid? UpdatedByUserId,
    bool HasProtectedValue);
