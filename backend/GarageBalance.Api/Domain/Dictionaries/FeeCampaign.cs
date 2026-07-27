namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class FeeCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public Guid IncomeTypeId { get; set; }
    public IncomeType IncomeType { get; set; } = null!;
    public string? Goal { get; set; }
    public decimal ContributionAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool AppliesToAllGarages { get; set; } = true;
    public ICollection<FeeCampaignGarage> ParticipantGarages { get; set; } = [];
    public int OverdueGraceDays { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public bool IsClosedEarly { get; set; }
    public string? ClosureComment { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
