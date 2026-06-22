using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseService(IWebHostEnvironment environment) : IAppReleaseService
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = NormalizeLimit(limit);
        var path = Path.Combine(environment.ContentRootPath, "AppReleases", "releases.json");

        if (!File.Exists(path))
        {
            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(
                "releases_file_missing",
                "Файл истории обновлений не найден.");
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var releases = await JsonSerializer.DeserializeAsync<List<AppReleaseDto>>(stream, JsonOptions, cancellationToken)
                ?? [];

            var invalidRelease = releases.FirstOrDefault(release =>
                string.IsNullOrWhiteSpace(release.ReleaseId) ||
                string.IsNullOrWhiteSpace(release.Version) ||
                string.IsNullOrWhiteSpace(release.Title) ||
                string.IsNullOrWhiteSpace(release.Summary));

            if (invalidRelease is not null)
            {
                return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(
                    "release_invalid",
                    $"Запись истории обновлений {invalidRelease.ReleaseId} заполнена не полностью.");
            }

            var sorted = releases
                .OrderByDescending(release => release.PublishedAt)
                .ThenByDescending(release => release.Version, StringComparer.OrdinalIgnoreCase)
                .Take(normalizedLimit)
                .ToArray();

            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Success(sorted);
        }
        catch (JsonException)
        {
            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(
                "releases_file_invalid",
                "Файл истории обновлений содержит некорректный JSON.");
        }
        catch (IOException)
        {
            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(
                "releases_file_unavailable",
                "Файл истории обновлений временно недоступен.");
        }
    }

    private static int NormalizeLimit(int? limit)
    {
        if (limit is null or <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit.Value, MaxLimit);
    }
}
