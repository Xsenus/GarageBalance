using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Integrations;

public sealed record OneCFreshSyncRequest([MaxLength(1000)] string? Comment);

public sealed record OneCFreshSyncPreviewDto(
    Guid AuditEventId,
    string Provider,
    string Mode,
    string Direction,
    string Status,
    string StatusMessage,
    DateTimeOffset RequestedAtUtc,
    string PeriodSummary,
    string SnapshotHash,
    bool CanApply,
    IReadOnlyList<OneCFreshSyncPreviewCountDto> Counts,
    IReadOnlyList<OneCFreshSyncPreviewNoticeDto> Warnings,
    IReadOnlyList<OneCFreshSyncPreviewNoticeDto> Conflicts);

public sealed record OneCFreshSyncPreviewCountDto(string ObjectType, string Operation, int Count);

public sealed record OneCFreshSyncPreviewNoticeDto(string Code, string Message);

public sealed record OneCFreshSyncDto(
    Guid AuditEventId,
    string Provider,
    string Status,
    string StatusMessage,
    DateTimeOffset RequestedAtUtc,
    bool IsRetry = false,
    bool CanRetry = false,
    bool HasConflict = false,
    string? ErrorCode = null,
    string? ExternalRunId = null,
    string? RecoveryAction = null);

public sealed record OneCFreshSyncResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static OneCFreshSyncResult<T> Success(T value) => new(true, value, null, null);

    public static OneCFreshSyncResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
