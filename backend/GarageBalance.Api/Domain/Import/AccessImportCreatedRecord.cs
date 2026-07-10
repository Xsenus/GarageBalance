namespace GarageBalance.Api.Domain.Import;

public sealed class AccessImportCreatedRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessImportRunId { get; set; }
    public string SourceSystem { get; set; } = "Access";
    public string SourceEntityType { get; set; } = string.Empty;
    public string? SourceExternalId { get; set; }
    public string SourceRowHash { get; set; } = string.Empty;
    public string TargetEntityType { get; set; } = string.Empty;
    public string TargetEntityId { get; set; } = string.Empty;
    public string? TargetDisplayName { get; set; }
    public string RollbackStatus { get; set; } = "created";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? RolledBackAtUtc { get; set; }
    public Guid? RolledBackByUserId { get; set; }
    public string? RollbackReason { get; set; }
}
