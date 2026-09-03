namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class StaffSalaryRatePeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public StaffMember StaffMember { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public decimal Rate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
