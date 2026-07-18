using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public sealed class Accrual
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
    public Guid IncomeTypeId { get; set; }
    public IncomeType IncomeType { get; set; } = null!;
    public Guid? TariffId { get; set; }
    public Tariff? Tariff { get; set; }
    public DateOnly AccountingMonth { get; set; }
    public int? AccountingYear { get; set; }
    public DateOnly DueDate { get; set; }
    public DateOnly OverdueFromDate { get; set; }
    public bool DueDateNeedsReview { get; set; }
    public string? DueDateReviewReason { get; set; }
    public decimal Amount { get; set; }
    public required string Source { get; set; }
    public string? Comment { get; set; }
    public bool IsCanceled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
