namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class Tariff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string CalculationBase { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Comment { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
