namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class FeeCampaignGarage
{
    public Guid FeeCampaignId { get; set; }
    public FeeCampaign FeeCampaign { get; set; } = null!;
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
}
