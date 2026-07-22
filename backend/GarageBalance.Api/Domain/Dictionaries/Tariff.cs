namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class Tariff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string CalculationBase { get; set; }
    public decimal Rate { get; set; }
    public decimal? ElectricityFirstThreshold { get; set; }
    public decimal? ElectricitySecondThreshold { get; set; }
    public string? ElectricityFirstTierName { get; set; }
    public string? ElectricitySecondTierName { get; set; }
    public string? ElectricityThirdTierName { get; set; }
    public decimal? ElectricityFirstRate { get; set; }
    public decimal? ElectricitySecondRate { get; set; }
    public decimal? ElectricityThirdRate { get; set; }
    public string? ElectricityTiersJson { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Comment { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
