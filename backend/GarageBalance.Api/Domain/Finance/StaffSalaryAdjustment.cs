using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class StaffSalaryAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffMemberId { get; set; }
    public StaffMember StaffMember { get; set; } = null!;
    public DateOnly AccountingMonth { get; set; }
    public required string AdjustmentType { get; set; }
    public decimal Amount { get; set; }
    public string? DocumentNumber { get; set; }
    public required string Reason { get; set; }
    public bool IsCanceled { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
