using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class IncomeType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Code { get; set; }
    public Guid? DestinationFundId { get; set; }
    public Fund? DestinationFund { get; set; }
    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
