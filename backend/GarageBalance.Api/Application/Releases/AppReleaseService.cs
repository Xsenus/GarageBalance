using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;

namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseService(
    IWebHostEnvironment environment,
    GarageBalanceDbContext? dbContext = null,
    IAuditEventWriter? auditEventWriter = null) : IAppReleaseService
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 50;
    private const string EntityType = "app_release";

    private static readonly SemaphoreSlim FileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly ISet<string> AllowedItemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "new",
        "improved",
        "fixed",
        "important"
    };

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>
    {
        ["version"] = "Версия",
        ["publishedAt"] = "Дата публикации",
        ["title"] = "Заголовок",
        ["summary"] = "Описание",
        ["items"] = "Пункты",
        ["isPublished"] = "Опубликовано"
    };

    public async Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken)
    {
        var result = await LoadReleasesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var sorted = SortReleases(result.Value!)
            .Where(release => release.IsPublished is not false)
            .Take(NormalizeLimit(limit))
            .ToArray();

        return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Success(sorted);
    }

    public async Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetManageableReleasesAsync(int? limit, CancellationToken cancellationToken)
    {
        var result = await LoadReleasesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var sorted = SortReleases(result.Value!)
            .Take(NormalizeLimit(limit))
            .ToArray();

        return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Success(sorted);
    }

    public async Task<AppReleaseResult<AppReleaseDto>> CreateReleaseAsync(UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRequest(request, null);
        if (!normalized.Succeeded)
        {
            return normalized;
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var loadResult = await LoadReleasesAsync(cancellationToken);
            if (!loadResult.Succeeded)
            {
                return AppReleaseResult<AppReleaseDto>.Failure(loadResult.ErrorCode!, loadResult.ErrorMessage!);
            }

            var releases = loadResult.Value!.ToList();
            if (releases.Any(release => string.Equals(release.ReleaseId, normalized.Value!.ReleaseId, StringComparison.OrdinalIgnoreCase)))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_id", "Запись с таким идентификатором уже существует.");
            }

            if (releases.Any(release => string.Equals(release.Version, normalized.Value!.Version, StringComparison.OrdinalIgnoreCase)))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_version", "Запись с такой версией уже существует.");
            }

            releases.Add(normalized.Value!);
            await SaveReleasesAsync(releases, cancellationToken);
            await AddAuditAsync(
                actorUserId,
                "app_releases.release_created",
                normalized.Value!,
                "create",
                $"Создана запись \"Что нового\" {normalized.Value!.Version}.",
                null,
                ToAuditValues(normalized.Value!),
                cancellationToken);

            return AppReleaseResult<AppReleaseDto>.Success(normalized.Value!);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<AppReleaseResult<AppReleaseDto>> UpdateReleaseAsync(string releaseId, UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var loadResult = await LoadReleasesAsync(cancellationToken);
            if (!loadResult.Succeeded)
            {
                return AppReleaseResult<AppReleaseDto>.Failure(loadResult.ErrorCode!, loadResult.ErrorMessage!);
            }

            var releases = loadResult.Value!.ToList();
            var index = releases.FindIndex(release => string.Equals(release.ReleaseId, releaseId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_not_found", "Запись истории обновлений не найдена.");
            }

            var current = releases[index];
            var normalized = NormalizeRequest(request, current.ReleaseId);
            if (!normalized.Succeeded)
            {
                return normalized;
            }

            if (releases.Any(release =>
                    !string.Equals(release.ReleaseId, current.ReleaseId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(release.Version, normalized.Value!.Version, StringComparison.OrdinalIgnoreCase)))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_version", "Запись с такой версией уже существует.");
            }

            releases[index] = normalized.Value!;
            await SaveReleasesAsync(releases, cancellationToken);
            await AddAuditAsync(
                actorUserId,
                "app_releases.release_updated",
                normalized.Value!,
                "update",
                $"Обновлена запись \"Что нового\" {normalized.Value!.Version}.",
                ToAuditValues(current),
                ToAuditValues(normalized.Value!),
                cancellationToken);

            return AppReleaseResult<AppReleaseDto>.Success(normalized.Value!);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<AppReleaseResult<AppReleaseDto>> PublishReleaseAsync(string releaseId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var loadResult = await LoadReleasesAsync(cancellationToken);
            if (!loadResult.Succeeded)
            {
                return AppReleaseResult<AppReleaseDto>.Failure(loadResult.ErrorCode!, loadResult.ErrorMessage!);
            }

            var releases = loadResult.Value!.ToList();
            var index = releases.FindIndex(release => string.Equals(release.ReleaseId, releaseId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_not_found", "Запись истории обновлений не найдена.");
            }

            var current = releases[index];
            var published = current with
            {
                PublishedAt = DateTimeOffset.Now,
                IsPublished = true
            };

            releases[index] = published;
            await SaveReleasesAsync(releases, cancellationToken);
            await AddAuditAsync(
                actorUserId,
                "app_releases.release_published",
                published,
                "publish",
                $"Опубликована запись \"Что нового\" {published.Version}.",
                ToAuditValues(current),
                ToAuditValues(published),
                cancellationToken);

            return AppReleaseResult<AppReleaseDto>.Success(published);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> LoadReleasesAsync(CancellationToken cancellationToken)
    {
        var path = GetReleasesPath();

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
                string.IsNullOrWhiteSpace(release.Summary) ||
                release.Items.Count == 0 ||
                release.Items.Any(item => string.IsNullOrWhiteSpace(item.Type) || string.IsNullOrWhiteSpace(item.Text)));

            if (invalidRelease is not null)
            {
                return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Failure(
                    "release_invalid",
                    $"Запись истории обновлений {invalidRelease.ReleaseId} заполнена не полностью.");
            }

            return AppReleaseResult<IReadOnlyList<AppReleaseDto>>.Success(releases);
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

    private AppReleaseResult<AppReleaseDto> NormalizeRequest(UpsertAppReleaseRequest request, string? existingReleaseId)
    {
        var version = request.Version.Trim();
        var title = request.Title.Trim();
        var summary = request.Summary.Trim();
        var releaseId = string.IsNullOrWhiteSpace(existingReleaseId)
            ? NormalizeReleaseId(request.ReleaseId, version)
            : existingReleaseId;

        if (string.IsNullOrWhiteSpace(version))
        {
            return AppReleaseResult<AppReleaseDto>.Failure("release_version_required", "Укажите версию обновления.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return AppReleaseResult<AppReleaseDto>.Failure("release_title_required", "Укажите заголовок обновления.");
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return AppReleaseResult<AppReleaseDto>.Failure("release_summary_required", "Укажите краткое описание обновления.");
        }

        if (request.Items.Count == 0)
        {
            return AppReleaseResult<AppReleaseDto>.Failure("release_items_required", "Добавьте хотя бы один пункт обновления.");
        }

        var items = new List<AppReleaseItemDto>();
        foreach (var item in request.Items)
        {
            var itemType = item.Type.Trim().ToLowerInvariant();
            var text = item.Text.Trim();
            if (!AllowedItemTypes.Contains(itemType))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_item_type_invalid", "Тип пункта должен быть new, improved, fixed или important.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_item_text_required", "Текст пункта обновления не может быть пустым.");
            }

            items.Add(new AppReleaseItemDto(itemType, text));
        }

        return AppReleaseResult<AppReleaseDto>.Success(new AppReleaseDto(
            releaseId,
            version,
            request.PublishedAt ?? DateTimeOffset.Now,
            title,
            summary,
            items,
            request.IsPublished));
    }

    private async Task SaveReleasesAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken)
    {
        var path = GetReleasesPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(SortReleases(releases), JsonOptions), cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private async Task AddAuditAsync(
        Guid? actorUserId,
        string action,
        AppReleaseDto release,
        string actionKind,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues,
        CancellationToken cancellationToken)
    {
        if (dbContext is null || auditEventWriter is null)
        {
            return;
        }

        auditEventWriter.Add(new AuditEventWriteRequest(
            actorUserId,
            action,
            EntityType,
            release.ReleaseId,
            Summary: summary,
            Section: "app_releases",
            ActionKind: actionKind,
            EntityDisplayName: release.Title,
            OldValues: oldValues,
            NewValues: newValues,
            FieldLabels: FieldLabels,
            RelatedDocumentId: release.ReleaseId,
            RelatedDocumentNumber: release.Version));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetReleasesPath()
    {
        return Path.Combine(environment.ContentRootPath, "AppReleases", "releases.json");
    }

    private static IReadOnlyDictionary<string, object?> ToAuditValues(AppReleaseDto release)
    {
        return new Dictionary<string, object?>
        {
            ["version"] = release.Version,
            ["publishedAt"] = release.PublishedAt,
            ["title"] = release.Title,
            ["summary"] = release.Summary,
            ["items"] = string.Join("; ", release.Items.Select(item => $"{item.Type}: {item.Text}")),
            ["isPublished"] = release.IsPublished is not false
        };
    }

    private static string NormalizeReleaseId(string? requestedReleaseId, string version)
    {
        if (!string.IsNullOrWhiteSpace(requestedReleaseId))
        {
            return requestedReleaseId.Trim();
        }

        var normalizedVersion = new string(version
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray())
            .Trim('-');
        return $"{DateTimeOffset.Now:yyyy-MM-dd}-{normalizedVersion}";
    }

    private static IEnumerable<AppReleaseDto> SortReleases(IEnumerable<AppReleaseDto> releases)
    {
        return releases
            .OrderByDescending(release => release.PublishedAt)
            .ThenByDescending(release => release.Version, StringComparer.OrdinalIgnoreCase);
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
