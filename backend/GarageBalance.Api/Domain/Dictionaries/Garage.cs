namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class Garage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Number { get; set; }
    public int PeopleCount { get; set; }
    public int FloorCount { get; set; }
    public decimal? InitialWaterMeterValue { get; set; }
    public decimal? InitialElectricityMeterValue { get; set; }
    public string? Comment { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Guid? OwnerId { get; set; }
    public Owner? Owner { get; set; }
}
