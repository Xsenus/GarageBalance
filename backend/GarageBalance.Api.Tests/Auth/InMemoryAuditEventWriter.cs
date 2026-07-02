using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Tests.Auth;

internal sealed class InMemoryAuditEventWriter(InMemoryUserRepository repository) : IAuditEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AuditEvent? Add(AuditEventWriteRequest request)
    {
        var auditEvent = new AuditEvent
        {
            ActorUserId = request.ActorUserId,
            Action = request.Action,
            Section = request.Section ?? InferSection(request.Action),
            ActionKind = request.ActionKind ?? InferActionKind(request.Action),
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            EntityDisplayName = request.EntityDisplayName,
            RelatedGarageId = request.RelatedGarageId,
            RelatedGarageNumber = request.RelatedGarageNumber,
            RelatedAccountingMonth = request.RelatedAccountingMonth,
            RelatedCounterpartyId = request.RelatedCounterpartyId,
            RelatedCounterpartyName = request.RelatedCounterpartyName,
            RelatedDocumentId = request.RelatedDocumentId,
            RelatedDocumentNumber = request.RelatedDocumentNumber,
            Summary = AuditTextMasker.Mask(request.Summary) ?? "Изменение объекта.",
            MetadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(MaskMetadata(request.Metadata), JsonOptions)
        };

        repository.AuditEvents.Add(auditEvent);
        return auditEvent;
    }

    private static Dictionary<string, string?> MaskMetadata(IReadOnlyDictionary<string, object?> metadata)
    {
        var masked = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            if (value is not null)
            {
                masked[key] = AuditTextMasker.Mask(value.ToString());
            }
        }

        return masked;
    }

    private static string InferSection(string action)
    {
        var separatorIndex = action.IndexOf('.', StringComparison.Ordinal);
        return separatorIndex > 0 ? action[..separatorIndex] : "system";
    }

    private static string InferActionKind(string action)
    {
        var normalized = action.ToLowerInvariant();
        if (normalized.Contains("password_changed", StringComparison.Ordinal))
        {
            return "update";
        }

        if (normalized.StartsWith("auth.login", StringComparison.Ordinal))
        {
            return "login";
        }

        if (normalized.Contains("_created", StringComparison.Ordinal))
        {
            return "create";
        }

        return "other";
    }
}
