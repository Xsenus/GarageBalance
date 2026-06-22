namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class Owner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string LastName { get; set; }
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? MeterNotes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string FullName => string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(part => !string.IsNullOrWhiteSpace(part)));
    public List<Garage> Garages { get; set; } = [];
}
