using GarageBalance.Api.Domain.Common;

namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class GarageTariffAssignment : IOptimisticConcurrencyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
    public Guid ChargeServiceSettingId { get; set; }
    public ChargeServiceSetting ChargeServiceSetting { get; set; } = null!;
    public Guid TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsArchived { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid Version { get; set; } = Guid.NewGuid();
}
