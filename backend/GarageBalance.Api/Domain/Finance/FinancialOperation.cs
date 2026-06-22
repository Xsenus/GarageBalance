using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class FinancialOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OperationKind { get; set; }
    public DateOnly OperationDate { get; set; }
    public DateOnly AccountingMonth { get; set; }
    public decimal Amount { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Comment { get; set; }
    public Guid? GarageId { get; set; }
    public Garage? Garage { get; set; }
    public Guid? IncomeTypeId { get; set; }
    public IncomeType? IncomeType { get; set; }
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public Guid? ExpenseTypeId { get; set; }
    public ExpenseType? ExpenseType { get; set; }
    public bool IsCanceled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
