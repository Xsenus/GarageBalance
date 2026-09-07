namespace GarageBalance.Api.Domain.Finance;

public sealed class ExpensePaymentBatch
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public required string RequestHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ExpensePaymentBatchOperation> Operations { get; set; } = [];
}

public sealed class ExpensePaymentBatchOperation
{
    public Guid BatchId { get; set; }
    public ExpensePaymentBatch Batch { get; set; } = null!;
    public Guid OperationId { get; set; }
    public FinancialOperation Operation { get; set; } = null!;
}
