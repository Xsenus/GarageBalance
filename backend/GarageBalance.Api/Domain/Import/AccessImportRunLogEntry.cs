namespace GarageBalance.Api.Domain.Import;

public sealed class AccessImportRunLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessImportRunId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "info";
    public string StepCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
}
