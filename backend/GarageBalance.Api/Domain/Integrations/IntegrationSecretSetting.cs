namespace GarageBalance.Api.Domain.Integrations;

public sealed class IntegrationSecretSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string NormalizedProvider { get; set; } = string.Empty;
    public string NormalizedSettingKey { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
