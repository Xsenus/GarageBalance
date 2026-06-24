namespace GarageBalance.Api.Domain.Import;

public sealed class AccessImportRowFingerprint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FingerprintKey { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string RowHash { get; set; } = string.Empty;
    public Guid? AccessImportRunId { get; set; }
    public string? TargetEntityType { get; set; }
    public string? TargetEntityId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
