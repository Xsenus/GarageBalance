using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class MeterReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
    public Guid? MeterDeviceId { get; set; }
    public MeterDevice? MeterDevice { get; set; }
    public required string MeterKind { get; set; }
    public DateOnly AccountingMonth { get; set; }
    public DateOnly ReadingDate { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal PreviousValue { get; set; }
    public decimal PreviousDeviceConsumption { get; set; }
    public decimal Consumption { get; set; }
    public bool IsMeterReplacement { get; set; }
    public bool HasGapWarning { get; set; }
    public string? Comment { get; set; }
    public bool IsCanceled { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
