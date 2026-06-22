namespace GarageBalance.Api.Application.Releases;

public sealed record AppReleaseDto(
    string ReleaseId,
    string Version,
    DateTimeOffset PublishedAt,
    string Title,
    string Summary,
    IReadOnlyList<AppReleaseItemDto> Items);

public sealed record AppReleaseItemDto(string Type, string Text);
