namespace GarageBalance.Api.Domain.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public string? Section { get; set; }
    public string? ActionKind { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? EntityDisplayName { get; set; }
    public string? RelatedGarageId { get; set; }
    public string? RelatedGarageNumber { get; set; }
    public string? RelatedAccountingMonth { get; set; }
    public string? RelatedCounterpartyId { get; set; }
    public string? RelatedCounterpartyName { get; set; }
    public string? RelatedDocumentId { get; set; }
    public string? RelatedDocumentNumber { get; set; }
    public required string Summary { get; set; }
    public string? MetadataJson { get; set; }
}
