namespace GarageBalance.Api.Domain.Import;

public sealed class AccessImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Mode { get; set; } = "dry_run";
    public string Status { get; set; } = "completed";
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public int TotalChecks { get; set; }
    public int PassedChecks { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ReportJson { get; set; } = "[]";
}
