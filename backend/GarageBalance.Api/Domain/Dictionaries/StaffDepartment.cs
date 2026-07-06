namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class StaffDepartment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<StaffMember> StaffMembers { get; } = new List<StaffMember>();
}
