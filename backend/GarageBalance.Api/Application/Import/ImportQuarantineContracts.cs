namespace GarageBalance.Api.Application.Import;

public sealed record RegisterImportQuarantineItemRequest(
    string SourceSystem,
    string EntityType,
    string? ExternalId,
    string RowHash,
    string ReasonCode,
    string ReasonMessage,
    string Severity,
    string? RowSnapshotJson,
    Guid? AccessImportRunId);

public sealed record ResolveImportQuarantineItemRequest(string? ResolutionComment);

public sealed record AccessImportQuarantineItemDto(
    Guid Id,
    Guid? AccessImportRunId,
    string SourceSystem,
    string EntityType,
    string? ExternalId,
    string RowHash,
    string ReasonCode,
    string ReasonMessage,
    string Severity,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? ResolvedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolutionComment);
