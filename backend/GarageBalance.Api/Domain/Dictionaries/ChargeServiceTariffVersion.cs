namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class ChargeServiceTariffVersion
{
    public Guid ChargeServiceSettingId { get; set; }
    public ChargeServiceSetting ChargeServiceSetting { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsArchived { get; set; }
    public Guid TariffId { get; set; }
    public Tariff Tariff { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
