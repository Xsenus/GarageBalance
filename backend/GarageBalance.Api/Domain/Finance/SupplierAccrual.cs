using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class SupplierAccrual
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid ExpenseTypeId { get; set; }
    public ExpenseType ExpenseType { get; set; } = null!;
    public Guid? SourceFinancialOperationId { get; set; }
    public FinancialOperation? SourceFinancialOperation { get; set; }
    public DateOnly AccountingMonth { get; set; }
    public decimal Amount { get; set; }
    public required string Source { get; set; }
    public string? DocumentNumber { get; set; }
    public string? Comment { get; set; }
    public bool IsCanceled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
