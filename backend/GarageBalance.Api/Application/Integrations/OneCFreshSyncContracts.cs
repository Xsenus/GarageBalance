using System.ComponentModel.DataAnnotations;

namespace GarageBalance.Api.Application.Integrations;

public sealed record OneCFreshSyncRequest([MaxLength(1000)] string? Comment);

public sealed record OneCFreshSyncDto(
    Guid AuditEventId,
    string Provider,
    string Status,
    string StatusMessage,
    DateTimeOffset RequestedAtUtc);

public sealed record OneCFreshSyncResult<T>(bool Succeeded, T? Value, string? ErrorCode, string? ErrorMessage)
{
    public static OneCFreshSyncResult<T> Success(T value) => new(true, value, null, null);

    public static OneCFreshSyncResult<T> Failure(string errorCode, string errorMessage) => new(false, default, errorCode, errorMessage);
}
