using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Import;

public sealed class ImportFingerprintService(
    GarageBalanceDbContext dbContext,
    IAuditEventWriter auditEventWriter) : IImportFingerprintService
{
    public async Task<ImportResult<RegisterImportRowFingerprintDto>> RegisterAsync(
        RegisterImportRowFingerprintRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return ImportResult<RegisterImportRowFingerprintDto>.Failure(validationError.Value.Code, validationError.Value.Message);
        }

        var sourceSystem = request.SourceSystem.Trim();
        var entityType = request.EntityType.Trim();
        var externalId = NormalizeOptional(request.ExternalId);
        var rowHash = request.RowHash.Trim().ToLowerInvariant();
        var fingerprintKey = BuildFingerprintKey(sourceSystem, entityType, externalId, rowHash);

        var existing = await dbContext.AccessImportRowFingerprints
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.FingerprintKey == fingerprintKey, cancellationToken);

        if (existing is not null)
        {
            return ImportResult<RegisterImportRowFingerprintDto>.Success(new RegisterImportRowFingerprintDto(false, ToDto(existing)));
        }

        var fingerprint = new AccessImportRowFingerprint
        {
            FingerprintKey = fingerprintKey,
            SourceSystem = sourceSystem,
            EntityType = entityType,
            ExternalId = externalId,
            RowHash = rowHash,
            AccessImportRunId = request.AccessImportRunId,
            TargetEntityType = NormalizeOptional(request.TargetEntityType),
            TargetEntityId = NormalizeOptional(request.TargetEntityId),
            CreatedByUserId = actorUserId
        };

        dbContext.AccessImportRowFingerprints.Add(fingerprint);
        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            "import.row_fingerprint_registered",
            "access_import_row_fingerprint",
            fingerprintKey,
            Summary: $"Import fingerprint registered: {sourceSystem}/{entityType}.",
            ActionKind: "import",
            EntityDisplayName: $"{sourceSystem}/{entityType}",
            RelatedDocumentId: request.AccessImportRunId?.ToString(),
            RelatedDocumentNumber: externalId,
            Metadata: new Dictionary<string, object?>
            {
                ["sourceSystem"] = sourceSystem,
                ["importEntityType"] = entityType,
                ["externalId"] = externalId,
                ["rowHash"] = rowHash,
                ["accessImportRunId"] = request.AccessImportRunId,
                ["targetEntityType"] = fingerprint.TargetEntityType,
                ["targetEntityId"] = fingerprint.TargetEntityId
            }));

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<RegisterImportRowFingerprintDto>.Success(new RegisterImportRowFingerprintDto(true, ToDto(fingerprint)));
    }

    public Task<bool> ExistsAsync(
        string sourceSystem,
        string entityType,
        string? externalId,
        string rowHash,
        CancellationToken cancellationToken)
    {
        var key = BuildFingerprintKey(sourceSystem, entityType, NormalizeOptional(externalId), rowHash.Trim().ToLowerInvariant());
        return dbContext.AccessImportRowFingerprints.AnyAsync(item => item.FingerprintKey == key, cancellationToken);
    }

    private static (string Code, string Message)? ValidateRequest(RegisterImportRowFingerprintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceSystem))
        {
            return ("import_source_required", "Источник импорта обязателен.");
        }

        if (string.IsNullOrWhiteSpace(request.EntityType))
        {
            return ("import_entity_type_required", "Тип импортируемой сущности обязателен.");
        }

        if (string.IsNullOrWhiteSpace(request.RowHash))
        {
            return ("import_row_hash_required", "Хэш строки импорта обязателен.");
        }

        var rowHash = request.RowHash.Trim();
        if (rowHash.Length != 64 || rowHash.Any(character => !Uri.IsHexDigit(character)))
        {
            return ("import_row_hash_invalid", "Хэш строки импорта должен быть SHA-256 в hex-формате.");
        }

        if (request.SourceSystem.Trim().Length > 80 ||
            request.EntityType.Trim().Length > 120 ||
            NormalizeOptional(request.ExternalId)?.Length > 240 ||
            NormalizeOptional(request.TargetEntityType)?.Length > 120 ||
            NormalizeOptional(request.TargetEntityId)?.Length > 120)
        {
            return ("import_fingerprint_field_too_long", "Поля fingerprint импорта превышают допустимую длину.");
        }

        return null;
    }

    private static ImportRowFingerprintDto ToDto(AccessImportRowFingerprint fingerprint)
    {
        return new ImportRowFingerprintDto(
            fingerprint.Id,
            fingerprint.FingerprintKey,
            fingerprint.SourceSystem,
            fingerprint.EntityType,
            fingerprint.ExternalId,
            fingerprint.RowHash,
            fingerprint.AccessImportRunId,
            fingerprint.TargetEntityType,
            fingerprint.TargetEntityId,
            fingerprint.CreatedAtUtc,
            fingerprint.CreatedByUserId);
    }

    private static string BuildFingerprintKey(string sourceSystem, string entityType, string? externalId, string rowHash)
    {
        var normalizedSource = NormalizeRequired(sourceSystem);
        var normalizedEntity = NormalizeRequired(entityType);
        var normalizedIdentity = string.IsNullOrWhiteSpace(externalId)
            ? $"hash:{rowHash.Trim().ToLowerInvariant()}"
            : $"external:{NormalizeRequired(externalId)}";

        return $"{normalizedSource}|{normalizedEntity}|{normalizedIdentity}";
    }

    private static string NormalizeRequired(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
