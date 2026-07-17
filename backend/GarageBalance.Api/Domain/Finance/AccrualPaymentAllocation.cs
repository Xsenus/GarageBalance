namespace GarageBalance.Api.Domain.Finance;

public sealed class AccrualPaymentAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FinancialOperationId { get; set; }
    public FinancialOperation FinancialOperation { get; set; } = null!;
    public Guid AccrualId { get; set; }
    public Accrual Accrual { get; set; } = null!;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
