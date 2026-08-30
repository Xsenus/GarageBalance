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
        if (IsNpgsqlProvider())
        {
            return await GetPostgresPageAsync(query, offset, limit, cancellationToken);
        }

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

    private async Task<AppReleasePageDto> GetPostgresPageAsync(
        IQueryable<AppReleaseRecord> query,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        const int PageCategory = 1;
        const int TotalsCategory = 2;
        var pageRows = query
            .OrderByDescending(release => release.PublishedAt)
            .ThenByDescending(release => release.Version)
            .Skip(offset)
            .Take(limit)
            .Select(release => new AppReleasePageRow
            {
                Category = PageCategory,
                ReleaseId = release.ReleaseId,
                Version = release.Version,
                PublishedAt = (DateTimeOffset?)release.PublishedAt,
                Title = release.Title,
                Summary = release.Summary,
                ItemsJson = release.ItemsJson,
                IsPublished = (bool?)release.IsPublished,
                TotalCount = 0
            });
        var totalsRow = dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
            .Select(_ => new AppReleasePageRow
            {
                Category = TotalsCategory,
                ReleaseId = null,
                Version = null,
                PublishedAt = null,
                Title = null,
                Summary = null,
                ItemsJson = null,
                IsPublished = null,
                TotalCount = query.Count()
            });
        var rows = await pageRows
            .Concat(totalsRow)
            .OrderBy(row => row.Category)
            .ThenByDescending(row => row.PublishedAt)
            .ThenByDescending(row => row.Version)
            .ToArrayAsync(cancellationToken);
        var totalCount = rows.Single(row => row.Category == TotalsCategory).TotalCount;
        var items = rows
            .Where(row => row.Category == PageCategory)
            .Select(ToDto)
            .ToArray();
        return new AppReleasePageDto(items, totalCount, offset, limit, offset + items.Length < totalCount);
    }

    public async Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken)
    {
        var releaseIds = releases.Select(release => release.ReleaseId).ToArray();
        var existingReleaseIds = releaseIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : await dbContext.AppReleases
                .Where(record => releaseIds.Contains(record.ReleaseId))
                .Select(record => record.ReleaseId)
                .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        foreach (var release in releases)
        {
            if (existingReleaseIds.Add(release.ReleaseId))
            {
                ApplyRelease(release, null);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AppReleaseDto?> FindAsync(string releaseId, CancellationToken cancellationToken)
    {
        var record = await dbContext.AppReleases
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ReleaseId == releaseId, cancellationToken);
        return record is null ? null : ToDto(record);
    }

    public Task<bool> VersionExistsAsync(string version, string? excludedReleaseId, CancellationToken cancellationToken)
    {
        var normalizedVersion = version.ToLower();
        return dbContext.AppReleases.AnyAsync(
            item => item.Version.ToLower() == normalizedVersion && (excludedReleaseId == null || item.ReleaseId != excludedReleaseId),
            cancellationToken);
    }

    public async Task StageUpsertAsync(AppReleaseDto release, CancellationToken cancellationToken)
    {
        var record = await dbContext.AppReleases
            .SingleOrDefaultAsync(item => item.ReleaseId == release.ReleaseId, cancellationToken);
        ApplyRelease(release, record);
    }

    private void ApplyRelease(AppReleaseDto release, AppReleaseRecord? record)
    {
        if (record is null)
        {
            record = new AppReleaseRecord { ReleaseId = release.ReleaseId };
            dbContext.AppReleases.Add(record);
        }

        record.Version = release.Version;
        record.PublishedAt = release.PublishedAt.ToUniversalTime();
        record.Title = release.Title;
        record.Summary = release.Summary;
        record.ItemsJson = JsonSerializer.Serialize(release.Items, JsonOptions);
        record.IsPublished = release.IsPublished is not false;
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

    private static AppReleaseDto ToDto(AppReleasePageRow release)
    {
        var items = JsonSerializer.Deserialize<AppReleaseItemDto[]>(release.ItemsJson!, JsonOptions) ?? [];
        return new AppReleaseDto(
            release.ReleaseId!,
            release.Version!,
            release.PublishedAt!.Value,
            release.Title!,
            release.Summary!,
            items,
            release.IsPublished!.Value);
    }

    private bool IsNpgsqlProvider() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private sealed class AppReleasePageRow
    {
        public int Category { get; init; }
        public string? ReleaseId { get; init; }
        public string? Version { get; init; }
        public DateTimeOffset? PublishedAt { get; init; }
        public string? Title { get; init; }
        public string? Summary { get; init; }
        public string? ItemsJson { get; init; }
        public bool? IsPublished { get; init; }
        public int TotalCount { get; init; }
    }
}
