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
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
