namespace GarageBalance.Api.Domain.Workflows;

public sealed class FormState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Scope { get; set; }
    public required string PayloadJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }
}
