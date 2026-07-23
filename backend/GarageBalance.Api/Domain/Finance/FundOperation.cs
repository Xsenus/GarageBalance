namespace GarageBalance.Api.Domain.Finance;

public sealed class FundOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FundId { get; set; }
    public Fund Fund { get; set; } = null!;
    public Guid? SourceFinancialOperationId { get; set; }
    public FinancialOperation? SourceFinancialOperation { get; set; }
    public string OperationKind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsCanceled { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
