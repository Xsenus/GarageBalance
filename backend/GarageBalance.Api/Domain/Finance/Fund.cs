namespace GarageBalance.Api.Domain.Finance;

public sealed class Fund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int SortOrder { get; set; }
    public bool AllowOperations { get; set; } = true;
    public bool IsSystem { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<FundOperation> Operations { get; set; } = [];
}
