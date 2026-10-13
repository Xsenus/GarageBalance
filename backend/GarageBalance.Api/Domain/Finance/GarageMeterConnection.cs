using GarageBalance.Api.Domain.Common;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

/// <summary>An explicitly dated accounting period for one garage meter chain.</summary>
public sealed class GarageMeterConnection : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
    public required string MeterKind { get; set; }
    public DateOnly ConnectedOn { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public Guid? BaselineMeterDeviceId { get; set; }
    public MeterDevice? BaselineMeterDevice { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
