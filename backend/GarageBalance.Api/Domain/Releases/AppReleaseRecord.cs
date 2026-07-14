namespace GarageBalance.Api.Domain.Releases;

public sealed class AppReleaseRecord
{
    public string ReleaseId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ItemsJson { get; set; } = "[]";
    public bool IsPublished { get; set; }
}
