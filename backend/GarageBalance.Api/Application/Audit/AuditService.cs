using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Application.Reports;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GarageBalance.Api.Application.Audit;

public sealed class AuditService(GarageBalanceDbContext dbContext) : IAuditService
{
    private const int DefaultListLimit = 100;
    private const int MaxListLimit = 500;
    private static readonly string[] ExportHeaders =
    [
        "createdAtUtc",
        "actorUserId",
        "section",
        "actionKind",
        "action",
        "entityType",
        "entityId",
        "entityDisplayName",
        "relatedGarageId",
        "relatedGarageNumber",
        "relatedAccountingMonth",
        "relatedCounterpartyId",
        "relatedCounterpartyName",
        "relatedDocumentId",
        "relatedDocumentNumber",
        "fieldName",
        "oldValue",
        "newValue",
        "reason",
        "metadata",
        "summary"
    ];

    public async Task<IReadOnlyList<AuditEventDto>> GetEventsAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return await GetEventsForSqliteAsync(query, request, limit, cancellationToken);
        }

        if (request.DateFrom is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        query = ApplyNonDateFilters(query, request);

        return await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(limit)
            .Select(auditEvent => ToDto(auditEvent))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditEventPageDto> GetEventsPageAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var offset = NormalizeOffset(request.Offset);
        var query = dbContext.AuditEvents.AsNoTracking();

        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            return await GetEventsPageForSqliteAsync(query, request, offset, limit, cancellationToken);
        }

        if (request.DateFrom is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value);
        }

        if (request.DateTo is not null)
        {
            query = query.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value);
        }

        query = ApplyNonDateFilters(query, request);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .Select(auditEvent => ToDto(auditEvent))
            .ToListAsync(cancellationToken);

        return new AuditEventPageDto(items, totalCount, offset, limit);
    }

    private static AuditEventDto ToDto(AuditEvent auditEvent)
    {
        var maskedSummary = AuditTextMasker.Mask(auditEvent.Summary) ?? string.Empty;
        var beforeAfter = ExtractBeforeAfter(maskedSummary);
        var metadata = ParseMetadata(auditEvent.MetadataJson);

        return new AuditEventDto(
            auditEvent.Id,
            auditEvent.CreatedAtUtc,
            auditEvent.ActorUserId,
            auditEvent.Action,
            auditEvent.EntityType,
            AuditTextMasker.Mask(auditEvent.EntityId),
            maskedSummary,
            MaskStoredValue(auditEvent.Section) ?? GetSection(auditEvent.Action),
            MaskStoredValue(auditEvent.ActionKind) ?? GetActionKind(auditEvent.Action),
            ExtractFieldName(maskedSummary),
            beforeAfter.OldValue,
            beforeAfter.NewValue,
            ExtractReason(maskedSummary),
            metadata,
            MaskStoredValue(auditEvent.EntityDisplayName) ?? ExtractEntityDisplayName(metadata),
            MaskStoredValue(auditEvent.RelatedGarageId) ?? ExtractMetadataValue(metadata, "relatedGarageId", "garageId"),
            MaskStoredValue(auditEvent.RelatedGarageNumber) ?? ExtractMetadataValue(metadata, "relatedGarageNumber", "garageNumber"),
            MaskStoredValue(auditEvent.RelatedAccountingMonth) ?? ExtractMetadataValue(metadata, "relatedAccountingMonth", "accountingMonth", "period", "month"),
            MaskStoredValue(auditEvent.RelatedCounterpartyId) ?? ExtractMetadataValue(metadata, "relatedCounterpartyId", "counterpartyId", "supplierId", "ownerId", "employeeId"),
            MaskStoredValue(auditEvent.RelatedCounterpartyName) ?? ExtractMetadataValue(metadata, "relatedCounterpartyName", "counterpartyName", "supplierName", "ownerName", "employeeName"),
            MaskStoredValue(auditEvent.RelatedDocumentId) ?? ExtractMetadataValue(metadata, "relatedDocumentId", "documentId", "operationId", "paymentId", "accrualId", "invoiceId", "receiptId"),
            MaskStoredValue(auditEvent.RelatedDocumentNumber) ?? ExtractMetadataValue(metadata, "relatedDocumentNumber", "operationNumber", "documentNumber", "paymentNumber", "invoiceNumber", "receiptNumber"));
    }

    private static async Task<IReadOnlyList<AuditEventDto>> GetEventsForSqliteAsync(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request,
        int limit,
        CancellationToken cancellationToken)
    {
        query = ApplyNonDateFilters(query, request);

        var events = await query.ToListAsync(cancellationToken);

        if (request.DateFrom is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value).ToList();
        }

        if (request.DateTo is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value).ToList();
        }

        return events
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Take(limit)
            .Select(ToDto)
            .ToList();
    }

    private static async Task<AuditEventPageDto> GetEventsPageForSqliteAsync(
        IQueryable<AuditEvent> query,
        AuditEventListRequest request,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        query = ApplyNonDateFilters(query, request);
        var events = await query.ToListAsync(cancellationToken);

        if (request.DateFrom is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc >= request.DateFrom.Value).ToList();
        }

        if (request.DateTo is not null)
        {
            events = events.Where(auditEvent => auditEvent.CreatedAtUtc <= request.DateTo.Value).ToList();
        }

        var totalCount = events.Count;
        var items = events
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .Select(ToDto)
            .ToList();

        return new AuditEventPageDto(items, totalCount, offset, limit);
    }

    public async Task<AuditEventExportDto> ExportEventsCsvAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var events = await GetEventsAsync(request, cancellationToken);
        var csv = BuildCsv(events);
        var fileName = $"audit-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv";

        return new AuditEventExportDto(fileName, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv));
    }

    public async Task<AuditEventExportDto> ExportEventsXlsxAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var events = await GetEventsAsync(request, cancellationToken);
        var content = XlsxWorkbookBuilder.Build(
        [
            new XlsxSheet(
                "История изменений",
                ExportHeaders,
                events.Select(auditEvent => (IReadOnlyList<XlsxCell>)BuildExportCells(auditEvent)).ToList())
        ]);
        var fileName = $"audit-events-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.xlsx";

        return new AuditEventExportDto(fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", content);
    }

    public async Task<AuditEventDto?> GetEventAsync(Guid id, CancellationToken cancellationToken)
    {
        var auditEvent = await dbContext.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(auditEvent => auditEvent.Id == id, cancellationToken);

        return auditEvent is null ? null : ToDto(auditEvent);
    }

    private static string BuildCsv(IReadOnlyList<AuditEventDto> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', ExportHeaders));

        foreach (var auditEvent in events)
        {
            builder.AppendLine(string.Join(',', BuildExportValues(auditEvent).Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string?> BuildExportValues(AuditEventDto auditEvent) =>
    [
        auditEvent.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        auditEvent.ActorUserId?.ToString(),
        auditEvent.Section,
        auditEvent.ActionKind,
        auditEvent.Action,
        auditEvent.EntityType,
        auditEvent.EntityId,
        auditEvent.EntityDisplayName,
        auditEvent.RelatedGarageId,
        auditEvent.RelatedGarageNumber,
        auditEvent.RelatedAccountingMonth,
        auditEvent.RelatedCounterpartyId,
        auditEvent.RelatedCounterpartyName,
        auditEvent.RelatedDocumentId,
        auditEvent.RelatedDocumentNumber,
        auditEvent.FieldName,
        auditEvent.OldValue,
        auditEvent.NewValue,
        auditEvent.Reason,
        FormatMetadata(auditEvent.Metadata),
        auditEvent.Summary
    ];

    private static IReadOnlyList<XlsxCell> BuildExportCells(AuditEventDto auditEvent) =>
        BuildExportValues(auditEvent).Select(XlsxCell.Text).ToList();

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Any(character => character is ',' or '"' or '\r' or '\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultListLimit;
        }

        return Math.Min(limit.Value, MaxListLimit);
    }

    private static int NormalizeOffset(int? offset)
    {
        return offset is null or < 0 ? 0 : offset.Value;
    }

    private static IQueryable<AuditEvent> ApplyNonDateFilters(IQueryable<AuditEvent> query, AuditEventListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var action = request.Action.Trim();
            query = query.Where(auditEvent => auditEvent.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(request.Section))
        {
            query = ApplySectionFilter(query, request.Section);
        }

        if (!string.IsNullOrWhiteSpace(request.ActionKind))
        {
            query = ApplyActionKindFilter(query, request.ActionKind);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var entityType = request.EntityType.Trim();
            query = query.Where(auditEvent => auditEvent.EntityType == entityType);
        }

        if (request.ActorUserId is not null)
        {
            query = query.Where(auditEvent => auditEvent.ActorUserId == request.ActorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.QuickFilter))
        {
            query = ApplyQuickFilter(query, request.QuickFilter);
        }

        query = ApplyRelatedFilters(query, request);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(search) ||
                auditEvent.EntityType.ToLower().Contains(search) ||
                (auditEvent.EntityId != null && auditEvent.EntityId.ToLower().Contains(search)) ||
                (auditEvent.EntityDisplayName != null && auditEvent.EntityDisplayName.ToLower().Contains(search)) ||
                (auditEvent.RelatedGarageId != null && auditEvent.RelatedGarageId.ToLower().Contains(search)) ||
                (auditEvent.RelatedGarageNumber != null && auditEvent.RelatedGarageNumber.ToLower().Contains(search)) ||
                (auditEvent.RelatedAccountingMonth != null && auditEvent.RelatedAccountingMonth.ToLower().Contains(search)) ||
                (auditEvent.RelatedCounterpartyId != null && auditEvent.RelatedCounterpartyId.ToLower().Contains(search)) ||
                (auditEvent.RelatedCounterpartyName != null && auditEvent.RelatedCounterpartyName.ToLower().Contains(search)) ||
                (auditEvent.RelatedDocumentId != null && auditEvent.RelatedDocumentId.ToLower().Contains(search)) ||
                (auditEvent.RelatedDocumentNumber != null && auditEvent.RelatedDocumentNumber.ToLower().Contains(search)) ||
                auditEvent.Summary.ToLower().Contains(search));
        }

        return query;
    }

    private static IQueryable<AuditEvent> ApplyRelatedFilters(IQueryable<AuditEvent> query, AuditEventListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RelatedGarage))
        {
            var garage = request.RelatedGarage.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedGarageId != null && auditEvent.RelatedGarageId.ToLower().Contains(garage)) ||
                (auditEvent.RelatedGarageNumber != null && auditEvent.RelatedGarageNumber.ToLower().Contains(garage)));
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedAccountingMonth))
        {
            var accountingMonth = request.RelatedAccountingMonth.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                auditEvent.RelatedAccountingMonth != null &&
                auditEvent.RelatedAccountingMonth.ToLower() == accountingMonth);
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedCounterparty))
        {
            var counterparty = request.RelatedCounterparty.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedCounterpartyId != null && auditEvent.RelatedCounterpartyId.ToLower().Contains(counterparty)) ||
                (auditEvent.RelatedCounterpartyName != null && auditEvent.RelatedCounterpartyName.ToLower().Contains(counterparty)));
        }

        if (!string.IsNullOrWhiteSpace(request.RelatedDocument))
        {
            var document = request.RelatedDocument.Trim().ToLowerInvariant();
            query = query.Where(auditEvent =>
                (auditEvent.RelatedDocumentId != null && auditEvent.RelatedDocumentId.ToLower().Contains(document)) ||
                (auditEvent.RelatedDocumentNumber != null && auditEvent.RelatedDocumentNumber.ToLower().Contains(document)));
        }

        return query;
    }

    private static IReadOnlyList<string> GetActionKindNeedles(string actionKind)
    {
        return actionKind.Trim().ToLowerInvariant() switch
        {
            "create" => ["_created"],
            "update" => ["_updated", "password_changed"],
            "archive" => ["_archived"],
            "restore" => ["_restored"],
            "cancel" => ["_canceled", "_cancelled"],
            "delete" => ["_deleted"],
            "login" => ["login_"],
            "fail" => ["_failed", "_rate_limited", "_inactive"],
            "generate" => ["_generated"],
            "import" => ["import."],
            "export" => ["_exported", ".export"],
            _ => []
        };
    }

    private static IQueryable<AuditEvent> ApplySectionFilter(IQueryable<AuditEvent> query, string section)
    {
        var normalizedSection = section.Trim().ToLowerInvariant();
        var sectionPrefix = normalizedSection + ".";
        return query.Where(auditEvent =>
            (auditEvent.Section != null && auditEvent.Section.ToLower() == normalizedSection) ||
            (auditEvent.Section == null && auditEvent.Action.ToLower().StartsWith(sectionPrefix)));
    }

    private static string GetSection(string action)
    {
        var separatorIndex = action.IndexOf('.', StringComparison.Ordinal);
        return separatorIndex > 0 ? action[..separatorIndex] : "system";
    }

    private static string GetActionKind(string action)
    {
        var normalized = action.ToLowerInvariant();

        if (normalized.Contains("_created", StringComparison.Ordinal))
        {
            return "create";
        }

        if (normalized.Contains("_updated", StringComparison.Ordinal) || normalized.Contains("password_changed", StringComparison.Ordinal))
        {
            return "update";
        }

        if (normalized.Contains("_archived", StringComparison.Ordinal))
        {
            return "archive";
        }

        if (normalized.Contains("_restored", StringComparison.Ordinal))
        {
            return "restore";
        }

        if (normalized.Contains("_canceled", StringComparison.Ordinal) || normalized.Contains("_cancelled", StringComparison.Ordinal))
        {
            return "cancel";
        }

        if (normalized.Contains("_deleted", StringComparison.Ordinal))
        {
            return "delete";
        }

        if (normalized.Contains("_failed", StringComparison.Ordinal) || normalized.Contains("_rate_limited", StringComparison.Ordinal) || normalized.Contains("_inactive", StringComparison.Ordinal))
        {
            return "fail";
        }

        if (normalized.Contains("_generated", StringComparison.Ordinal))
        {
            return "generate";
        }

        if (normalized.StartsWith("auth.login", StringComparison.Ordinal))
        {
            return "login";
        }

        if (normalized.StartsWith("import.", StringComparison.Ordinal))
        {
            return "import";
        }

        if (normalized.Contains("_exported", StringComparison.Ordinal) || normalized.Contains(".export", StringComparison.Ordinal))
        {
            return "export";
        }

        return "other";
    }

    private static string? ExtractFieldName(string summary)
    {
        var match = Regex.Match(summary, @"поле\s+(?<field>.+?):\s+было", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeExtractedValue(match.Groups["field"].Value) : null;
    }

    private static (string? OldValue, string? NewValue) ExtractBeforeAfter(string summary)
    {
        var match = Regex.Match(summary, @"было\s+(?<old>.+?);?\s+стало\s+(?<new>.+?)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
            ? (NormalizeExtractedValue(match.Groups["old"].Value), NormalizeExtractedValue(match.Groups["new"].Value))
            : (null, null);
    }

    private static string? ExtractReason(string summary)
    {
        var match = Regex.Match(summary, @"(?:Причина|Комментарий):\s*(?<reason>.+?)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? NormalizeExtractedValue(match.Groups["reason"].Value) : null;
    }

    private static string? NormalizeExtractedValue(string value)
    {
        var normalized = value.Trim().TrimEnd(';', '.').Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyDictionary<string, string>? ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject().Take(20))
                {
                    metadata[property.Name] = FormatMetadataValue(property.Name, property.Value);
                }
            }
            else
            {
                metadata["value"] = FormatMetadataValue("value", document.RootElement);
            }

            return metadata.Count == 0 ? null : metadata;
        }
        catch (JsonException)
        {
            var maskedMetadata = AuditTextMasker.Mask(metadataJson);
            return string.IsNullOrWhiteSpace(maskedMetadata)
                ? null
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["raw"] = maskedMetadata };
        }
    }

    private static string FormatMetadataValue(string key, JsonElement value)
    {
        if (IsSensitiveMetadataKey(key))
        {
            return "[секрет скрыт]";
        }

        var rawValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };

        return AuditTextMasker.Mask(rawValue) ?? string.Empty;
    }

    private static bool IsSensitiveMetadataKey(string key)
    {
        var normalized = key.ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("connectionstring", StringComparison.Ordinal) ||
            normalized.Contains("api_key", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("private_key", StringComparison.Ordinal) ||
            normalized.Contains("privatekey", StringComparison.Ordinal);
    }

    private static string? FormatMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        return metadata is null || metadata.Count == 0
            ? null
            : string.Join("; ", metadata.Select(item => $"{item.Key}={item.Value}"));
    }

    private static string? MaskStoredValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : AuditTextMasker.Mask(value);
    }

    private static string? ExtractEntityDisplayName(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var explicitName = ExtractMetadataValue(metadata, "entityDisplayName", "displayName", "objectName", "name", "title");

        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }

        var garageNumber = ExtractMetadataValue(metadata, "garageNumber", "relatedGarageNumber");
        if (!string.IsNullOrWhiteSpace(garageNumber))
        {
            return $"Гараж {garageNumber}";
        }

        var documentNumber = ExtractMetadataValue(metadata, "documentNumber", "relatedDocumentNumber");
        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            return $"Документ {documentNumber}";
        }

        return ExtractMetadataValue(metadata, "period", "month", "relatedAccountingMonth", "accountingMonth");
    }

    private static string? ExtractMetadataValue(IReadOnlyDictionary<string, string>? metadata, params string[] keys)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var value = GetMetadataValue(metadata, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? GetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
    {
        var value = metadata
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            .Value;

        return string.IsNullOrWhiteSpace(value) || value == "[секрет скрыт]"
            ? null
            : value;
    }

    private static IQueryable<AuditEvent> ApplyActionKindFilter(IQueryable<AuditEvent> query, string actionKind)
    {
        var normalizedActionKind = actionKind.Trim().ToLowerInvariant();
        var needles = GetActionKindNeedles(actionKind);
        return needles.Count switch
        {
            1 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && auditEvent.Action.ToLower().Contains(needles[0]))),
            2 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && (auditEvent.Action.ToLower().Contains(needles[0]) || auditEvent.Action.ToLower().Contains(needles[1])))),
            3 => query.Where(auditEvent =>
                (auditEvent.ActionKind != null && auditEvent.ActionKind.ToLower() == normalizedActionKind) ||
                (auditEvent.ActionKind == null && (
                    auditEvent.Action.ToLower().Contains(needles[0]) ||
                    auditEvent.Action.ToLower().Contains(needles[1]) ||
                    auditEvent.Action.ToLower().Contains(needles[2])))),
            _ => query
        };
    }

    private static IQueryable<AuditEvent> ApplyQuickFilter(IQueryable<AuditEvent> query, string quickFilter)
    {
        return quickFilter.Trim().ToLowerInvariant() switch
        {
            "deletions" => query.Where(auditEvent =>
                auditEvent.ActionKind == "archive" ||
                auditEvent.ActionKind == "delete" ||
                auditEvent.ActionKind == "cancel" ||
                (auditEvent.ActionKind == null && (
                    auditEvent.Action.ToLower().Contains("_archived") ||
                    auditEvent.Action.ToLower().Contains("_deleted") ||
                    auditEvent.Action.ToLower().Contains("_canceled") ||
                    auditEvent.Action.ToLower().Contains("_cancelled")))),
            "restores" => query.Where(auditEvent =>
                auditEvent.ActionKind == "restore" ||
                (auditEvent.ActionKind == null && auditEvent.Action.ToLower().Contains("_restored"))),
            "financial" => query.Where(auditEvent =>
                auditEvent.Section == "finance" ||
                (auditEvent.Section == null && auditEvent.Action.ToLower().StartsWith("finance.")) ||
                auditEvent.Action.ToLower().Contains("fund") ||
                auditEvent.EntityType == "financial_operation" ||
                auditEvent.EntityType == "accrual" ||
                auditEvent.EntityType == "supplier_accrual" ||
                auditEvent.EntityType == "fund_operation"),
            _ => query
        };
    }
}
