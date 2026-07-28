using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Reports;

public sealed class GarageReportQuickList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public Guid? ArchivedByUserId { get; set; }
    public ICollection<GarageReportQuickListGarage> Garages { get; set; } = [];
}

public sealed class GarageReportQuickListGarage
{
    public Guid QuickListId { get; set; }
    public GarageReportQuickList QuickList { get; set; } = null!;
    public Guid GarageId { get; set; }
    public Garage Garage { get; set; } = null!;
}
