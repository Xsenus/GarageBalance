using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Import;

public sealed record AccessImportDryRunRequest(
    [Required] string FileName,
    [Required] Stream Content);

public sealed record AccessImportRunListRequest
{
    [Range(1, 200)]
    public int Limit { get; init; } = 50;
}

public sealed record AccessImportRunLogListRequest
{
    [Range(1, 500)]
    public int Limit { get; init; } = 100;
}

public sealed record AccessImportCreatedRecordListRequest
{
    [Range(1, 500)]
    public int Limit { get; init; } = 100;
}

public sealed record AccessImportRollbackRequest
{
    [Required]
    [MaxLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record AccessImportApplyRequest
{
    [Required]
    [MaxLength(1000)]
    public string Reason { get; init; } = string.Empty;

    public bool BackupConfirmed { get; init; }
}

public sealed record AccessImportApplyCancelRequest
{
    [Required]
    [MaxLength(1000)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record AccessImportCheckDto(
    string Code,
    string Title,
    string Status,
    string Message);

public sealed record AccessImportRunDto(
    Guid Id,
    string Mode,
    string Status,
    string OriginalFileName,
    string FileExtension,
    long FileSizeBytes,
    string ContentSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary,
    IReadOnlyList<AccessImportCheckDto> Checks);

public sealed record AccessImportRunLogEntryDto(
    Guid Id,
    Guid AccessImportRunId,
    DateTimeOffset CreatedAtUtc,
    string Level,
    string StepCode,
    string Message);

public sealed record AccessImportCreatedRecordDto(
    Guid Id,
    Guid AccessImportRunId,
    string SourceSystem,
    string SourceEntityType,
    string? SourceExternalId,
    string SourceRowHash,
    string TargetEntityType,
    string TargetEntityId,
    string? TargetDisplayName,
    string RollbackStatus,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? RolledBackAtUtc,
    Guid? RolledBackByUserId,
    string? RollbackReason);

public sealed record ImportReportFileDto(
    string FileName,
    string ContentType,
    byte[] Content);
