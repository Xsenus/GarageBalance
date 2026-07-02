using GarageBalance.Api.Domain.Audit;

namespace GarageBalance.Api.Application.Audit;

public interface IAuditEventWriter
{
    AuditEvent? Add(AuditEventWriteRequest request);
}

public sealed record AuditEventWriteRequest(
    Guid? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    string? Summary = null,
    string? Section = null,
    string? ActionKind = null,
    string? EntityDisplayName = null,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? OldValues = null,
    IReadOnlyDictionary<string, object?>? NewValues = null,
    IReadOnlyDictionary<string, string>? FieldLabels = null,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    string? RelatedGarageId = null,
    string? RelatedGarageNumber = null,
    string? RelatedAccountingMonth = null,
    string? RelatedCounterpartyId = null,
    string? RelatedCounterpartyName = null,
    string? RelatedDocumentId = null,
    string? RelatedDocumentNumber = null);
