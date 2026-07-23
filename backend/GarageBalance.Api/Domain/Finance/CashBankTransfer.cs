namespace GarageBalance.Api.Domain.Finance;

public sealed class CashBankTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly TransferDate { get; set; }
    public decimal Amount { get; set; }
    public string? Comment { get; set; }
    public bool IsCanceled { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
