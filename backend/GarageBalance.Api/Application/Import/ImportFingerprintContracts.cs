namespace GarageBalance.Api.Application.Import;

public sealed record RegisterImportRowFingerprintRequest(
    string SourceSystem,
    string EntityType,
    string? ExternalId,
    string RowHash,
    Guid? AccessImportRunId,
    string? TargetEntityType,
    string? TargetEntityId);

public sealed record ImportRowFingerprintDto(
    Guid Id,
    string FingerprintKey,
    string SourceSystem,
    string EntityType,
    string? ExternalId,
    string RowHash,
    Guid? AccessImportRunId,
    string? TargetEntityType,
    string? TargetEntityId,
    DateTimeOffset CreatedAtUtc,
    Guid? CreatedByUserId);

public sealed record RegisterImportRowFingerprintDto(
    bool Created,
    ImportRowFingerprintDto Fingerprint);
