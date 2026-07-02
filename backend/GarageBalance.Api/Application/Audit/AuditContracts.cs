namespace GarageBalance.Api.Application.Audit;

public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string Summary,
    string? Section = null,
    string? ActionKind = null,
    string? FieldName = null,
    string? OldValue = null,
    string? NewValue = null,
    string? Reason = null);

public sealed record AuditEventPageDto(
    IReadOnlyList<AuditEventDto> Items,
    int TotalCount,
    int Offset,
    int Limit);

public sealed record AuditEventListRequest(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    string? Action,
    string? Search,
    int? Limit = null,
    string? Section = null,
    string? ActionKind = null,
    string? EntityType = null,
    Guid? ActorUserId = null,
    string? QuickFilter = null,
    int? Offset = null);

public sealed record AuditEventExportDto(
    string FileName,
    string ContentType,
    byte[] Content);
