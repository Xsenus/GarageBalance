namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Inn { get; set; }
    public string? LegalAddress { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public decimal StartingBalance { get; set; }
    public string? Comment { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid GroupId { get; set; }
    public SupplierGroup Group { get; set; } = null!;
}
