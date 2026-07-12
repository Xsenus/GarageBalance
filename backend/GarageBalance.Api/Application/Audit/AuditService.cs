using GarageBalance.Api.Domain.Audit;
using GarageBalance.Api.Application.Reports;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GarageBalance.Api.Application.Audit;

public sealed class AuditService(IAuditEventRepository repository) : IAuditService
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
        var events = await repository.GetEventsAsync(request, limit, cancellationToken);
        return events.Select(ToDto).ToList();
    }

    public async Task<AuditEventPageDto> GetEventsPageAsync(AuditEventListRequest request, CancellationToken cancellationToken)
    {
        var limit = NormalizeLimit(request.Limit);
        var offset = NormalizeOffset(request.Offset);
        var page = await repository.GetEventsPageAsync(request, offset, limit, cancellationToken);
        return new AuditEventPageDto(page.Items.Select(ToDto).ToList(), page.TotalCount, offset, limit);
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
        var auditEvent = await repository.FindEventAsync(id, cancellationToken);

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

}
