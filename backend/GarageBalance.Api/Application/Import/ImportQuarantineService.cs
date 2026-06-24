using System.Text.Json;
using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Domain.Import;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Application.Import;

public sealed class ImportQuarantineService(GarageBalanceDbContext dbContext) : IImportQuarantineService
{
    private static readonly HashSet<string> AllowedSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "warning"
    };

    public async Task<IReadOnlyList<AccessImportQuarantineItemDto>> GetOpenItemsAsync(Guid? accessImportRunId, CancellationToken cancellationToken)
    {
        var query = dbContext.AccessImportQuarantineItems
            .AsNoTracking()
            .Where(item => item.Status == "open");

        if (accessImportRunId.HasValue)
        {
            query = query.Where(item => item.AccessImportRunId == accessImportRunId.Value);
        }

        List<AccessImportQuarantineItem> items;
        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            items = (await query.ToListAsync(cancellationToken))
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToList();
        }
        else
        {
            items = await query
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .ToListAsync(cancellationToken);
        }

        return items.Select(ToDto).ToList();
    }

    public async Task<ImportResult<AccessImportQuarantineItemDto>> RegisterAsync(
        RegisterImportQuarantineItemRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRegisterRequest(request, out var rowSnapshotJson);
        if (validationError is not null)
        {
            return ImportResult<AccessImportQuarantineItemDto>.Failure(validationError.Value.Code, validationError.Value.Message);
        }

        var sourceSystem = request.SourceSystem.Trim();
        var entityType = request.EntityType.Trim();
        var externalId = NormalizeOptional(request.ExternalId);
        var rowHash = request.RowHash.Trim().ToLowerInvariant();
        var reasonCode = request.ReasonCode.Trim();
        var reasonMessage = request.ReasonMessage.Trim();
        var severity = request.Severity.Trim().ToLowerInvariant();

        var item = new AccessImportQuarantineItem
        {
            AccessImportRunId = request.AccessImportRunId,
            SourceSystem = sourceSystem,
            EntityType = entityType,
            ExternalId = externalId,
            RowHash = rowHash,
            ReasonCode = reasonCode,
            ReasonMessage = reasonMessage,
            Severity = severity,
            RowSnapshotJson = rowSnapshotJson,
            CreatedByUserId = actorUserId
        };

        dbContext.AccessImportQuarantineItems.Add(item);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = "import.quarantine_registered",
            EntityType = "access_import_quarantine_item",
            EntityId = item.Id.ToString(),
            Summary = $"Import quarantine item registered: {sourceSystem}/{entityType}/{reasonCode}."
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportQuarantineItemDto>.Success(ToDto(item));
    }

    public async Task<ImportResult<AccessImportQuarantineItemDto>> ResolveAsync(
        Guid id,
        ResolveImportQuarantineItemRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.AccessImportQuarantineItems.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (item is null)
        {
            return ImportResult<AccessImportQuarantineItemDto>.Failure("import_quarantine_item_not_found", "Строка карантина импорта не найдена.");
        }

        if (item.Status == "resolved")
        {
            return ImportResult<AccessImportQuarantineItemDto>.Success(ToDto(item));
        }

        var resolutionComment = NormalizeOptional(request.ResolutionComment);
        if (resolutionComment?.Length > 1000)
        {
            return ImportResult<AccessImportQuarantineItemDto>.Failure("import_quarantine_resolution_too_long", "Комментарий закрытия карантина импорта превышает допустимую длину.");
        }

        item.Status = "resolved";
        item.ResolutionComment = resolutionComment;
        item.ResolvedAtUtc = DateTimeOffset.UtcNow;
        item.ResolvedByUserId = actorUserId;

        dbContext.AuditEvents.Add(new AuditEvent
        {
            ActorUserId = actorUserId,
            Action = "import.quarantine_resolved",
            EntityType = "access_import_quarantine_item",
            EntityId = item.Id.ToString(),
            Summary = $"Import quarantine item resolved: {item.SourceSystem}/{item.EntityType}/{item.ReasonCode}."
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return ImportResult<AccessImportQuarantineItemDto>.Success(ToDto(item));
    }

    private static (string Code, string Message)? ValidateRegisterRequest(RegisterImportQuarantineItemRequest request, out string rowSnapshotJson)
    {
        rowSnapshotJson = string.IsNullOrWhiteSpace(request.RowSnapshotJson) ? "{}" : request.RowSnapshotJson.Trim();

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

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return ("import_quarantine_reason_code_required", "Код причины карантина обязателен.");
        }

        if (string.IsNullOrWhiteSpace(request.ReasonMessage))
        {
            return ("import_quarantine_reason_required", "Описание причины карантина обязательно.");
        }

        if (string.IsNullOrWhiteSpace(request.Severity) || !AllowedSeverities.Contains(request.Severity.Trim()))
        {
            return ("import_quarantine_severity_invalid", "Уровень карантина импорта должен быть error или warning.");
        }

        if (request.SourceSystem.Trim().Length > 80 ||
            request.EntityType.Trim().Length > 120 ||
            NormalizeOptional(request.ExternalId)?.Length > 240 ||
            request.ReasonCode.Trim().Length > 120 ||
            request.ReasonMessage.Trim().Length > 1000)
        {
            return ("import_quarantine_field_too_long", "Поля карантина импорта превышают допустимую длину.");
        }

        try
        {
            using var _ = JsonDocument.Parse(rowSnapshotJson);
        }
        catch (JsonException)
        {
            return ("import_quarantine_snapshot_invalid", "Снимок строки карантина должен быть корректным JSON.");
        }

        return null;
    }

    private static AccessImportQuarantineItemDto ToDto(AccessImportQuarantineItem item)
    {
        return new AccessImportQuarantineItemDto(
            item.Id,
            item.AccessImportRunId,
            item.SourceSystem,
            item.EntityType,
            item.ExternalId,
            item.RowHash,
            item.ReasonCode,
            item.ReasonMessage,
            item.Severity,
            item.Status,
            item.CreatedAtUtc,
            item.CreatedByUserId,
            item.ResolvedAtUtc,
            item.ResolvedByUserId,
            item.ResolutionComment);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
