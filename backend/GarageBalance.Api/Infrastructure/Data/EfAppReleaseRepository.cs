using System.Text.Json;
using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Domain.Releases;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfAppReleaseRepository(GarageBalanceDbContext dbContext) : IAppReleaseRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AppReleasePageDto> GetPageAsync(
        bool includeDrafts,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AppReleases
            .AsNoTracking()
            .Where(release => includeDrafts || release.IsPublished);
        var totalCount = await query.CountAsync(cancellationToken);
        AppReleaseRecord[] records;
        if (string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
        {
            // SQLite is used only by tests and cannot order DateTimeOffset values.
            // PostgreSQL keeps the bounded server-side branch below.
            records = (await query.ToArrayAsync(cancellationToken))
                .OrderByDescending(release => release.PublishedAt)
                .ThenByDescending(release => release.Version, StringComparer.OrdinalIgnoreCase)
                .Skip(offset)
                .Take(limit)
                .ToArray();
        }
        else
        {
            records = await query
                .OrderByDescending(release => release.PublishedAt)
                .ThenByDescending(release => release.Version)
                .Skip(offset)
                .Take(limit)
                .ToArrayAsync(cancellationToken);
        }
        var items = records.Select(ToDto).ToArray();

        return new AppReleasePageDto(items, totalCount, offset, limit, offset + items.Length < totalCount);
    }

    public async Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken)
    {
        var releaseIds = releases.Select(release => release.ReleaseId).ToArray();
        var existingRecords = releaseIds.Length == 0
            ? new Dictionary<string, AppReleaseRecord>(StringComparer.Ordinal)
            : await dbContext.AppReleases
                .Where(record => releaseIds.Contains(record.ReleaseId))
                .ToDictionaryAsync(record => record.ReleaseId, StringComparer.Ordinal, cancellationToken);

        foreach (var release in releases)
        {
            if (!existingRecords.TryGetValue(release.ReleaseId, out var record))
            {
                record = new AppReleaseRecord { ReleaseId = release.ReleaseId };
                dbContext.AppReleases.Add(record);
            }

            record.Version = release.Version;
            record.PublishedAt = release.PublishedAt;
            record.Title = release.Title;
            record.Summary = release.Summary;
            record.ItemsJson = JsonSerializer.Serialize(release.Items, JsonOptions);
            record.IsPublished = release.IsPublished is not false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AppReleaseDto ToDto(AppReleaseRecord release)
    {
        var items = JsonSerializer.Deserialize<AppReleaseItemDto[]>(release.ItemsJson, JsonOptions) ?? [];
        return new AppReleaseDto(
            release.ReleaseId,
            release.Version,
            release.PublishedAt,
            release.Title,
            release.Summary,
            items,
            release.IsPublished);
    }
}
