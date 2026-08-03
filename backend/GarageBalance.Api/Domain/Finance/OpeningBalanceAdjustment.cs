namespace GarageBalance.Api.Domain.Finance;

public sealed class OpeningBalanceAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string TargetKind { get; set; }
    public Guid TargetId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal NewAmount { get; set; }
    public required string Reason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class OpeningBalanceAdjustmentTargetKinds
{
    public const string Garage = "garage";
    public const string Supplier = "supplier";
}
