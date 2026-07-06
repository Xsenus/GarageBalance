namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class ChargeServiceSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsRegular { get; set; }
    public int? PeriodicityMonths { get; set; }
    public int? AccrualStartMonth { get; set; }
    public int? PaymentDueDay { get; set; }
    public int? PaymentDueMonth { get; set; }
    public int OverdueGraceDays { get; set; }
    public Guid? IncomeTypeId { get; set; }
    public IncomeType? IncomeType { get; set; }
    public Guid? TariffId { get; set; }
    public Tariff? Tariff { get; set; }
    public bool IsMetered { get; set; }
    public bool HasTieredTariff { get; set; }
    public string? UnitName { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
