namespace GarageBalance.Api.Application.Releases;

public sealed record AppReleaseDto(
    string ReleaseId,
    string Version,
    DateTimeOffset PublishedAt,
    string Title,
    string Summary,
    IReadOnlyList<AppReleaseItemDto> Items,
    bool? IsPublished = null);

public sealed record AppReleaseItemDto(string Type, string Text);

public sealed record AppReleasePageDto(
    IReadOnlyList<AppReleaseDto> Items,
    int TotalCount,
    int Offset,
    int Limit,
    bool HasMore);

public sealed record UpsertAppReleaseRequest(
    string? ReleaseId,
    string Version,
    DateTimeOffset? PublishedAt,
    string Title,
    string Summary,
    IReadOnlyList<AppReleaseItemDto> Items,
    bool IsPublished = false);
