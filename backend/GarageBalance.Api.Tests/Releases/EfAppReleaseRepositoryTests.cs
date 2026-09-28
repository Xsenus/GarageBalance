using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Releases;

public sealed class EfAppReleaseRepositoryTests
{
    [Fact]
    public async Task UpsertAsync_InsertsAndUpdatesOnlyRequestedRelease()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        var first = CreateRelease("release-1", "0.1.0", "Первый релиз");
        var second = CreateRelease("release-2", "0.2.0", "Второй релиз");
        await repository.SynchronizeAsync([first, second], CancellationToken.None);

        await repository.StageUpsertAsync(
            CreateRelease("release-1", "Preview-1", "Обновлённый первый релиз"),
            CancellationToken.None);
        await context.SaveChangesAsync();

        var records = await context.AppReleases
            .AsNoTracking()
            .OrderBy(release => release.ReleaseId)
            .ToArrayAsync();
        Assert.Equal(2, records.Length);
        Assert.Equal("Preview-1", records[0].Version);
        Assert.Equal("Обновлённый первый релиз", records[0].Title);
        Assert.Equal("0.2.0", records[1].Version);
        Assert.Equal("Второй релиз", records[1].Title);
        Assert.Equal("release-1", (await repository.FindAsync("release-1", CancellationToken.None))!.ReleaseId);
        Assert.True(await repository.VersionExistsAsync("preview-1", null, CancellationToken.None));
        Assert.True(await repository.VersionExistsAsync("PREVIEW-1", null, CancellationToken.None));
        Assert.False(await repository.VersionExistsAsync("preview-1", "release-1", CancellationToken.None));
    }

    [Fact]
    public async Task GetPageAsync_ReturnsPublishedDatabaseRowsInStablePages()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        var releases = Enumerable.Range(1, 12)
            .Select(index => new AppReleaseDto(
                $"release-{index}",
                $"0.{index}.0",
                DateTimeOffset.Parse("2026-07-14T10:00:00+07:00").AddMinutes(index),
                $"Обновление {index}",
                "Описание.",
                [new AppReleaseItemDto("improved", "Изменение.")],
                index != 12))
            .ToArray();
        await repository.SynchronizeAsync(releases, CancellationToken.None);

        var storedRelease = await context.AppReleases
            .AsNoTracking()
            .SingleAsync(release => release.ReleaseId == "release-1");
        Assert.Equal(TimeSpan.Zero, storedRelease.PublishedAt.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-07-14T03:01:00Z"), storedRelease.PublishedAt);

        var firstPage = await repository.GetPageAsync(false, 0, 9, CancellationToken.None);
        var secondPage = await repository.GetPageAsync(false, 9, 9, CancellationToken.None);
        var manageablePage = await repository.GetPageAsync(true, 0, 9, CancellationToken.None);

        Assert.Equal(11, firstPage.TotalCount);
        Assert.Equal(9, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);
        Assert.Equal("release-11", firstPage.Items[0].ReleaseId);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.False(secondPage.HasMore);
        Assert.Equal(12, manageablePage.TotalCount);
        Assert.Equal("release-12", manageablePage.Items[0].ReleaseId);
    }

    [Fact]
    public async Task SynchronizeAsync_DoesNotOverwriteDatabaseManagedRelease()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GarageBalanceDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new GarageBalanceDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        await repository.SynchronizeAsync(
            [CreateRelease("release-1", "0.1.0", "Исходный заголовок")],
            CancellationToken.None);
        await repository.StageUpsertAsync(
            CreateRelease("release-1", "0.1.1", "Изменено администратором"),
            CancellationToken.None);
        await context.SaveChangesAsync();

        await repository.SynchronizeAsync(
            [
                CreateRelease("release-1", "0.1.0", "Исходный заголовок"),
                CreateRelease("release-2", "0.2.0", "Новый релиз")
            ],
            CancellationToken.None);

        var records = await context.AppReleases.AsNoTracking().OrderBy(item => item.ReleaseId).ToArrayAsync();
        Assert.Equal(2, records.Length);
        Assert.Equal("0.1.1", records[0].Version);
        Assert.Equal("Изменено администратором", records[0].Title);
        Assert.Equal("release-2", records[1].ReleaseId);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("Preview-1", "PREVIEW-1")]
    public async Task SynchronizeAsync_PreservesExistingVersionWithDifferentIdAndImportsOtherNotes(string storedVersion, string sourceVersion)
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        var managed = CreateRelease("managed", storedVersion, "Запись администратора") with { IsPublished = false };
        await repository.SynchronizeAsync([managed], CancellationToken.None);
        var source = new[]
        {
            CreateRelease("source-collision", sourceVersion, "Не должен заменить запись"),
            CreateRelease("source-new", "0.2.0", "Новое обновление")
        };

        await repository.SynchronizeAsync(source, CancellationToken.None);
        await repository.SynchronizeAsync(source, CancellationToken.None);
        await repository.SynchronizeAsync([], CancellationToken.None);

        var records = await context.AppReleases.AsNoTracking().OrderBy(record => record.ReleaseId).ToArrayAsync();
        Assert.Equal(2, records.Length);
        AssertReleaseMatches(managed, await repository.FindAsync("managed", CancellationToken.None));
        AssertReleaseMatches(source[1], await repository.FindAsync("source-new", CancellationToken.None));
        Assert.Null(await repository.FindAsync("source-collision", CancellationToken.None));
    }

    [Fact]
    public async Task SynchronizeAsync_DeduplicatesSourceVersionsAndIdsWithoutReservingSkippedVersions()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        var first = CreateRelease("first", "Preview-1", "Первое описание");
        var second = CreateRelease("second", "0.2.0", "Второе описание");

        await repository.SynchronizeAsync(
            [first, CreateRelease("duplicate-version", "PREVIEW-1", "Дубликат версии"),
                CreateRelease("first", "0.2.0", "Дубликат ID"), second], CancellationToken.None);

        Assert.Equal(2, await context.AppReleases.CountAsync());
        AssertReleaseMatches(first, await repository.FindAsync("first", CancellationToken.None));
        AssertReleaseMatches(second, await repository.FindAsync("second", CancellationToken.None));
    }

    [Fact]
    public async Task SynchronizeAsync_CancelledLookupDoesNotInsertNotes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new GarageBalanceDbContext(new DbContextOptionsBuilder<GarageBalanceDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfAppReleaseRepository(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SynchronizeAsync(
            [CreateRelease("cancelled", "0.3.0", "Не сохранится")], cancellation.Token));

        Assert.Equal(0, await context.AppReleases.CountAsync());
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static void AssertReleaseMatches(AppReleaseDto expected, AppReleaseDto? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ReleaseId, actual.ReleaseId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.PublishedAt, actual.PublishedAt);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Summary, actual.Summary);
        Assert.Equal(expected.IsPublished, actual.IsPublished);
        Assert.Equal(expected.Items, actual.Items);
    }

    private static AppReleaseDto CreateRelease(string releaseId, string version, string title) =>
        new(
            releaseId,
            version,
            DateTimeOffset.Parse("2026-07-14T10:00:00+07:00"),
            title,
            "Описание.",
            [new AppReleaseItemDto("improved", "Изменение.")],
            true);
}
