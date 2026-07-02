namespace GarageBalance.Api.Application.Audit;

public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    Guid? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string Summary);

public sealed record AuditEventListRequest(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    string? Action,
    string? Search,
    int? Limit = null,
    string? Section = null,
    string? ActionKind = null,
    string? EntityType = null);

public sealed record AuditEventExportDto(
    string FileName,
    string ContentType,
    byte[] Content);
