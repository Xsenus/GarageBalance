namespace GarageBalance.Api.Domain.Dictionaries;

public sealed class GaragePeopleCountPeriod
{
    public Guid GarageId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public int PeopleCount { get; set; }
}
