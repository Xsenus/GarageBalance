namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class StaffMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FullName { get; set; }
    public decimal Rate { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid DepartmentId { get; set; }
    public StaffDepartment Department { get; set; } = null!;
}
