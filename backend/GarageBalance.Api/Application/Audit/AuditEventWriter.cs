using GarageBalance.Api.Domain.Audit;
using System.Globalization;
using System.Text.Json;

namespace GarageBalance.Api.Application.Audit;

public sealed class AuditEventWriter(IAuditEventStore store) : IAuditEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AuditEvent? Add(AuditEventWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = RequireTrimmed(request.Action, nameof(request.Action), 120);
        var entityType = RequireTrimmed(request.EntityType, nameof(request.EntityType), 120);
        var section = NormalizeOptional(request.Section, 80) ?? InferSection(action);
        var actionKind = NormalizeOptional(request.ActionKind, 40) ?? InferActionKind(action);
        var reason = NormalizeOptional(request.Reason, 500);
        EnsureReasonForDangerousAction(actionKind, reason);

        var changes = BuildChanges(request);
        if (HasExplicitDiff(request) && changes.Count == 0)
        {
            return null;
        }

        var metadata = BuildMetadata(request, changes);
        var summary = BuildSummary(request.Summary, changes, reason, actionKind);
        var auditEvent = new AuditEvent
        {
            ActorUserId = request.ActorUserId,
            Action = action,
            Section = section,
            ActionKind = actionKind,
            EntityType = entityType,
            EntityId = NormalizeOptional(request.EntityId, 120),
            EntityDisplayName = NormalizeOptional(request.EntityDisplayName, 256),
            RelatedGarageId = NormalizeOptional(request.RelatedGarageId, 120),
            RelatedGarageNumber = NormalizeOptional(request.RelatedGarageNumber, 80),
            RelatedAccountingMonth = NormalizeOptional(request.RelatedAccountingMonth, 32),
            RelatedCounterpartyId = NormalizeOptional(request.RelatedCounterpartyId, 120),
            RelatedCounterpartyName = NormalizeOptional(request.RelatedCounterpartyName, 256),
            RelatedDocumentId = NormalizeOptional(request.RelatedDocumentId, 120),
            RelatedDocumentNumber = NormalizeOptional(request.RelatedDocumentNumber, 120),
            Summary = summary,
            MetadataJson = metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata, JsonOptions)
        };

        store.Add(auditEvent);
        return auditEvent;
    }

    private static IReadOnlyList<AuditChangeDiff> BuildChanges(AuditEventWriteRequest request)
    {
        return HasExplicitDiff(request)
            ? AuditChangeDiffBuilder.Build(request.OldValues!, request.NewValues!, request.FieldLabels)
            : [];
    }

    private static bool HasExplicitDiff(AuditEventWriteRequest request)
    {
        return request.OldValues is not null && request.NewValues is not null;
    }

    private static Dictionary<string, string?> BuildMetadata(
        AuditEventWriteRequest request,
        IReadOnlyList<AuditChangeDiff> changes)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal);
        AddMetadata(metadata, request.Metadata);
        AddMetadata(metadata, "reason", request.Reason);
        AddMetadata(metadata, "entityDisplayName", request.EntityDisplayName);
        AddMetadata(metadata, "relatedGarageId", request.RelatedGarageId);
        AddMetadata(metadata, "relatedGarageNumber", request.RelatedGarageNumber);
        AddMetadata(metadata, "relatedAccountingMonth", request.RelatedAccountingMonth);
        AddMetadata(metadata, "relatedCounterpartyId", request.RelatedCounterpartyId);
        AddMetadata(metadata, "relatedCounterpartyName", request.RelatedCounterpartyName);
        AddMetadata(metadata, "relatedDocumentId", request.RelatedDocumentId);
        AddMetadata(metadata, "relatedDocumentNumber", request.RelatedDocumentNumber);

        if (changes.Count > 0)
        {
            AddMetadata(metadata, "fieldName", string.Join("; ", changes.Select(change => change.FieldName)));
            AddMetadata(metadata, "oldValue", FormatChanges(changes, change => change.OldValue));
            AddMetadata(metadata, "newValue", FormatChanges(changes, change => change.NewValue));
            AddMetadata(metadata, "changesCount", changes.Count);
            if (changes.Count > 1)
            {
                AddMetadata(metadata, "changedFields", string.Join(", ", changes.Select(change => change.FieldName)));
            }
        }

        return metadata;
    }

    private static string FormatChanges(
        IReadOnlyList<AuditChangeDiff> changes,
        Func<AuditChangeDiff, string?> valueSelector)
    {
        return changes.Count == 1
            ? FormatDisplayValue(valueSelector(changes[0]))
            : string.Join("; ", changes.Select(change => $"{change.FieldName}: {FormatDisplayValue(valueSelector(change))}"));
    }

    private static void AddMetadata(Dictionary<string, string?> metadata, IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var (key, value) in source)
        {
            AddMetadata(metadata, key, value);
        }
    }

    private static void AddMetadata(Dictionary<string, string?> metadata, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            return;
        }

        var formatted = FormatMetadataValue(key, value);
        if (!string.IsNullOrWhiteSpace(formatted))
        {
            metadata[key.Trim()] = formatted;
        }
    }

    private static string? FormatMetadataValue(string key, object value)
    {
        if (IsSensitiveMetadataKey(key))
        {
            return "[секрет скрыт]";
        }

        var formatted = value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
            double number => number.ToString("0.##", CultureInfo.InvariantCulture),
            float number => number.ToString("0.##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        return NormalizeOptional(AuditTextMasker.Mask(formatted), 500);
    }

    private static string BuildSummary(
        string? explicitSummary,
        IReadOnlyList<AuditChangeDiff> changes,
        string? reason,
        string actionKind)
    {
        var summary = NormalizeOptional(AuditTextMasker.Mask(explicitSummary), 900);
        if (summary is null)
        {
            summary = changes.Count switch
            {
                0 => "Изменение объекта.",
                1 => $"Изменение: поле {changes[0].FieldName}: было {FormatDisplayValue(changes[0].OldValue)}; стало {FormatDisplayValue(changes[0].NewValue)}.",
                _ => $"Изменение: {AuditChangeDiffBuilder.FormatSummary(changes)}."
            };
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var label = IsDangerousAction(actionKind) ? "Причина" : "Комментарий";
            summary = $"{summary.Trim().TrimEnd('.')}. {label}: {AuditTextMasker.Mask(reason)}.";
        }

        return RequireTrimmed(summary, nameof(explicitSummary), 1000);
    }

    private static string FormatDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(пусто)" : value;
    }

    private static void EnsureReasonForDangerousAction(string actionKind, string? reason)
    {
        if (IsDangerousAction(actionKind) && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Причина обязательна для удаления, архивирования и отмены.");
        }
    }

    private static bool IsDangerousAction(string actionKind)
    {
        return string.Equals(actionKind, "archive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionKind, "delete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actionKind, "cancel", StringComparison.OrdinalIgnoreCase);
    }

    private static string InferSection(string action)
    {
        var index = action.IndexOf('.', StringComparison.Ordinal);
        return index > 0 ? RequireTrimmed(action[..index], nameof(action), 80) : "system";
    }

    private static string InferActionKind(string action)
    {
        var normalized = action.ToLowerInvariant();
        if (normalized.Contains("_created", StringComparison.Ordinal))
        {
            return "create";
        }

        if (normalized.Contains("_updated", StringComparison.Ordinal) ||
            normalized.Contains("password_changed", StringComparison.Ordinal))
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

        if (normalized.Contains("_canceled", StringComparison.Ordinal) ||
            normalized.Contains("_cancelled", StringComparison.Ordinal))
        {
            return "cancel";
        }

        if (normalized.Contains("_deleted", StringComparison.Ordinal))
        {
            return "delete";
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

        if (normalized.Contains("_exported", StringComparison.Ordinal) ||
            normalized.Contains(".export", StringComparison.Ordinal))
        {
            return "export";
        }

        return "other";
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
            normalized.Contains("privatekey", StringComparison.Ordinal) ||
            normalized.Contains("email", StringComparison.Ordinal) ||
            normalized.Contains("mail", StringComparison.Ordinal) ||
            normalized.Contains("phone", StringComparison.Ordinal) ||
            normalized.Contains("address", StringComparison.Ordinal) ||
            normalized.Contains("bank", StringComparison.Ordinal) ||
            normalized.Contains("account", StringComparison.Ordinal) ||
            normalized.Contains("passport", StringComparison.Ordinal);
    }

    private static string RequireTrimmed(string? value, string parameterName, int maxLength)
    {
        var normalized = NormalizeOptional(value, maxLength);
        return normalized ?? throw new ArgumentException("Значение обязательно.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
