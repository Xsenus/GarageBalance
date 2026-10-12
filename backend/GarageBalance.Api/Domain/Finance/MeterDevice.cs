using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class MeterDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
    public required string MeterKind { get; set; }
    public required string SerialNumber { get; set; }
    public DateOnly InstalledOn { get; set; }
    public DateOnly? RemovedOn { get; set; }
    public decimal InitialValue { get; set; }
    public decimal? FinalValue { get; set; }
    public Guid? ReplacedDeviceId { get; set; }
    public DateOnly? ReplacementAccountingMonth { get; set; }
    public Guid? ReplacementReadingId { get; set; }
    public string? PreviousReadingSnapshotJson { get; set; }
    public bool HasReplacementSnapshot { get; set; }
    public string? ReplacementReason { get; set; }
    public bool IsCanceled { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
