using System.Collections.Concurrent;
using System.Text.Json;
using GarageBalance.Api.Application.Audit;
using GarageBalance.Api.Application.Common;
using Microsoft.AspNetCore.Hosting;

namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseService(
    IWebHostEnvironment environment,
    IApplicationUnitOfWork? unitOfWork = null,
    IAuditEventWriter? auditEventWriter = null,
    IAppReleaseRepository? releaseRepository = null) : IAppReleaseService
{
    private const int DefaultLimit = 9;
    private const int MaxLimit = 50;
    private const string EntityType = "app_release";

    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReleaseLocks =
        new(StringComparer.OrdinalIgnoreCase);

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

    public async Task<AppReleaseResult<AppReleasePageDto>> GetReleasesAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeOffset(offset);
        var normalizedLimit = NormalizeLimit(limit);
        if (releaseRepository is not null)
        {
            var page = await releaseRepository.GetPageAsync(false, normalizedOffset, normalizedLimit, cancellationToken);
            return AppReleaseResult<AppReleasePageDto>.Success(page);
        }

        var result = await LoadReleasesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return AppReleaseResult<AppReleasePageDto>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var sorted = SortReleases(result.Value!)
            .Where(release => release.IsPublished is not false)
            .ToArray();
        var items = sorted.Skip(normalizedOffset).Take(normalizedLimit).ToArray();

        return AppReleaseResult<AppReleasePageDto>.Success(new AppReleasePageDto(
            items,
            sorted.Length,
            normalizedOffset,
            normalizedLimit,
            normalizedOffset + items.Length < sorted.Length));
    }

    public async Task<AppReleaseResult<AppReleasePageDto>> GetManageableReleasesAsync(int? offset, int? limit, CancellationToken cancellationToken)
    {
        var normalizedOffset = NormalizeOffset(offset);
        var normalizedLimit = NormalizeLimit(limit);
        if (releaseRepository is not null)
        {
            var page = await releaseRepository.GetPageAsync(true, normalizedOffset, normalizedLimit, cancellationToken);
            return AppReleaseResult<AppReleasePageDto>.Success(page);
        }

        var result = await LoadReleasesAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return AppReleaseResult<AppReleasePageDto>.Failure(result.ErrorCode!, result.ErrorMessage!);
        }

        var sorted = SortReleases(result.Value!).ToArray();
        var items = sorted.Skip(normalizedOffset).Take(normalizedLimit).ToArray();

        return AppReleaseResult<AppReleasePageDto>.Success(new AppReleasePageDto(
            items,
            sorted.Length,
            normalizedOffset,
            normalizedLimit,
            normalizedOffset + items.Length < sorted.Length));
    }

    public async Task<AppReleaseResult<AppReleaseDto>> CreateReleaseAsync(UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRequest(request, null);
        if (!normalized.Succeeded)
        {
            return normalized;
        }

        if (releaseRepository is not null)
        {
            return await CreateDatabaseReleaseAsync(normalized.Value!, actorUserId, cancellationToken);
        }

        var releaseLock = GetReleaseLock(normalized.Value!.ReleaseId);
        await releaseLock.WaitAsync(cancellationToken);
        try
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
            }
            finally
            {
                FileLock.Release();
            }

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
            releaseLock.Release();
        }
    }

    public async Task<AppReleaseResult<AppReleaseDto>> UpdateReleaseAsync(string releaseId, UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (releaseRepository is not null)
        {
            return await UpdateDatabaseReleaseAsync(releaseId, request, actorUserId, cancellationToken);
        }

        var releaseLock = GetReleaseLock(releaseId);
        await releaseLock.WaitAsync(cancellationToken);
        try
        {
            AppReleaseDto current;
            AppReleaseDto updated;
            IReadOnlyDictionary<string, object?> oldAuditValues;
            IReadOnlyDictionary<string, object?> newAuditValues;
            string changeSummary;

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

                current = releases[index];
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

                updated = normalized.Value!;
                oldAuditValues = ToAuditValues(current);
                newAuditValues = ToAuditValues(updated);
                changeSummary = FormatReleaseChangeSummary(oldAuditValues, newAuditValues)!;
                if (changeSummary is null)
                {
                    return AppReleaseResult<AppReleaseDto>.Success(current);
                }

                releases[index] = updated;
                await SaveReleasesAsync(releases, cancellationToken);
            }
            finally
            {
                FileLock.Release();
            }

            await AddAuditAsync(
                actorUserId,
                "app_releases.release_updated",
                updated,
                "update",
                $"Обновлена запись \"Что нового\" {updated.Version}: {changeSummary}.",
                oldAuditValues,
                newAuditValues,
                cancellationToken);

            return AppReleaseResult<AppReleaseDto>.Success(updated);
        }
        finally
        {
            releaseLock.Release();
        }
    }

    public async Task<AppReleaseResult<AppReleaseDto>> PublishReleaseAsync(string releaseId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (releaseRepository is not null)
        {
            return await PublishDatabaseReleaseAsync(releaseId, actorUserId, cancellationToken);
        }

        var releaseLock = GetReleaseLock(releaseId);
        await releaseLock.WaitAsync(cancellationToken);
        try
        {
            AppReleaseDto current;
            AppReleaseDto published;

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

                current = releases[index];
                if (current.IsPublished is true)
                {
                    return AppReleaseResult<AppReleaseDto>.Success(current);
                }

                published = current with
                {
                    PublishedAt = DateTimeOffset.Now,
                    IsPublished = true
                };

                releases[index] = published;
                await SaveReleasesAsync(releases, cancellationToken);
            }
            finally
            {
                FileLock.Release();
            }

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
            releaseLock.Release();
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
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
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

    private async Task<AppReleaseResult<AppReleaseDto>> CreateDatabaseReleaseAsync(
        AppReleaseDto release,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (unitOfWork is null)
        {
            return DatabaseMutationNotConfigured();
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            if (await releaseRepository!.FindAsync(release.ReleaseId, cancellationToken) is not null)
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_id", "Запись с таким идентификатором уже существует.");
            }

            if (await releaseRepository.VersionExistsAsync(release.Version, null, cancellationToken))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_version", "Запись с такой версией уже существует.");
            }

            await releaseRepository.StageUpsertAsync(release, cancellationToken);
            StageAudit(
                actorUserId,
                "app_releases.release_created",
                release,
                "create",
                $"Создана запись \"Что нового\" {release.Version}.",
                null,
                ToAuditValues(release));
            return await CommitDatabaseReleaseAsync(release, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<AppReleaseResult<AppReleaseDto>> UpdateDatabaseReleaseAsync(
        string releaseId,
        UpsertAppReleaseRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (unitOfWork is null)
        {
            return DatabaseMutationNotConfigured();
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var current = await releaseRepository!.FindAsync(releaseId, cancellationToken);
            if (current is null)
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_not_found", "Запись истории обновлений не найдена.");
            }

            var normalized = NormalizeRequest(request, current.ReleaseId);
            if (!normalized.Succeeded)
            {
                return normalized;
            }

            var updated = normalized.Value!;
            if (await releaseRepository.VersionExistsAsync(updated.Version, current.ReleaseId, cancellationToken))
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_duplicate_version", "Запись с такой версией уже существует.");
            }

            var oldValues = ToAuditValues(current);
            var newValues = ToAuditValues(updated);
            var changeSummary = FormatReleaseChangeSummary(oldValues, newValues);
            if (changeSummary is null)
            {
                return AppReleaseResult<AppReleaseDto>.Success(current);
            }

            await releaseRepository.StageUpsertAsync(updated, cancellationToken);
            StageAudit(
                actorUserId,
                "app_releases.release_updated",
                updated,
                "update",
                $"Обновлена запись \"Что нового\" {updated.Version}: {changeSummary}.",
                oldValues,
                newValues);
            return await CommitDatabaseReleaseAsync(updated, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<AppReleaseResult<AppReleaseDto>> PublishDatabaseReleaseAsync(
        string releaseId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (unitOfWork is null)
        {
            return DatabaseMutationNotConfigured();
        }

        await FileLock.WaitAsync(cancellationToken);
        try
        {
            var current = await releaseRepository!.FindAsync(releaseId, cancellationToken);
            if (current is null)
            {
                return AppReleaseResult<AppReleaseDto>.Failure("release_not_found", "Запись истории обновлений не найдена.");
            }

            if (current.IsPublished is true)
            {
                return AppReleaseResult<AppReleaseDto>.Success(current);
            }

            var published = current with { PublishedAt = DateTimeOffset.Now, IsPublished = true };
            await releaseRepository.StageUpsertAsync(published, cancellationToken);
            StageAudit(
                actorUserId,
                "app_releases.release_published",
                published,
                "publish",
                $"Опубликована запись \"Что нового\" {published.Version}.",
                ToAuditValues(current),
                ToAuditValues(published));
            return await CommitDatabaseReleaseAsync(published, cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private static AppReleaseResult<AppReleaseDto> DatabaseMutationNotConfigured() =>
        AppReleaseResult<AppReleaseDto>.Failure(
            "releases_store_unavailable",
            "Хранилище истории обновлений временно недоступно для изменения.");

    private async Task<AppReleaseResult<AppReleaseDto>> CommitDatabaseReleaseAsync(
        AppReleaseDto release,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork!.SaveChangesAsync(cancellationToken);
            return AppReleaseResult<AppReleaseDto>.Success(release);
        }
        catch (ApplicationPersistenceConflictException)
        {
            return AppReleaseResult<AppReleaseDto>.Failure(
                "release_conflict",
                "Запись или версия уже была сохранена другим администратором. Обновите список и повторите действие.");
        }
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
        try
        {
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(SortReleases(releases), JsonOptions), cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static SemaphoreSlim GetReleaseLock(string releaseId) =>
        ReleaseLocks.GetOrAdd(releaseId, static _ => new SemaphoreSlim(1, 1));

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
        if (unitOfWork is null || !StageAudit(actorUserId, action, release, actionKind, summary, oldValues, newValues))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private bool StageAudit(
        Guid? actorUserId,
        string action,
        AppReleaseDto release,
        string actionKind,
        string summary,
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues)
    {
        if (auditEventWriter is null)
        {
            return false;
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
        return true;
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

    private static string? FormatReleaseChangeSummary(
        IReadOnlyDictionary<string, object?> oldValues,
        IReadOnlyDictionary<string, object?> newValues)
    {
        var changes = AuditChangeDiffBuilder.Build(oldValues, newValues, FieldLabels);
        return changes.Count == 0 ? null : AuditChangeDiffBuilder.FormatSummary(changes);
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
        return QueryLimits.NormalizeListSize(limit, DefaultLimit, MaxLimit);
    }

    private static int NormalizeOffset(int? offset) => Math.Max(offset ?? 0, 0);
}
