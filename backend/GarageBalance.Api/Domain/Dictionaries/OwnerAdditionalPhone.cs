namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class OwnerAdditionalPhone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string Phone { get; set; }
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Owner Owner { get; set; } = null!;
}
