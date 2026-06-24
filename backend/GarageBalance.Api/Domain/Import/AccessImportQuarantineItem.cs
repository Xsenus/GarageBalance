namespace GarageBalance.Api.Domain.Import;

public sealed class AccessImportQuarantineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccessImportRunId { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string RowHash { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string ReasonMessage { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
    public string RowSnapshotJson { get; set; } = "{}";
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionComment { get; set; }
}
