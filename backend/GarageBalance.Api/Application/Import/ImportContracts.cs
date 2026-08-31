using System.ComponentModel.DataAnnotations;
using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Import;

public sealed record AccessImportDryRunRequest(
    [Required] string FileName,
    [Required] Stream Content);

public sealed record QueuedAccessImportDryRunRequest(
    Guid RunId,
    [Required] string FileName,
    long FileSizeBytes,
    Guid? ActorUserId);

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
    [ActionComment]
    [MaxLength(1000)]
    public string? Reason { get; init; }
}

public sealed record AccessImportApplyRequest
{
    [ActionComment]
    [MaxLength(1000)]
    public string? Reason { get; init; }

    public bool BackupConfirmed { get; init; }
}

public sealed record AccessImportApplyCancelRequest
{
    [ActionComment]
    [MaxLength(1000)]
    public string? Reason { get; init; }
}

public sealed record AccessImportCheckDto(
    string Code,
    string Title,
    string Status,
    string Message);

public sealed record AccessImportReaderStatusDto(
    string Provider,
    string DisplayName,
    bool IsAvailable,
    string Status,
    string StatusMessage,
    IReadOnlyList<string> RequiredComponents,
    DateTimeOffset CheckedAtUtc);

public sealed record AccessImportReaderInspectionDto(
    bool Succeeded,
    string Status,
    string StatusMessage,
    IReadOnlyList<string> TableNames)
{
    public static AccessImportReaderInspectionDto Success(IReadOnlyList<string> tableNames) =>
        new(true, "ready", $"Структура Access прочитана: таблиц {tableNames.Count}.", tableNames);

    public static AccessImportReaderInspectionDto Unavailable(string status, string statusMessage) =>
        new(false, status, statusMessage, []);
}

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

public sealed record AccessImportRunListItemDto(
    Guid Id,
    string Status,
    string OriginalFileName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary);

public sealed record AccessImportRunStatusDto(
    Guid Id,
    string Status,
    DateTimeOffset? FinishedAtUtc,
    int TotalChecks,
    int PassedChecks,
    int WarningCount,
    int ErrorCount,
    string Summary);

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
