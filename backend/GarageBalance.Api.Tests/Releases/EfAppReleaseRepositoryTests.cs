using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Tests.Releases;

public sealed class EfAppReleaseRepositoryTests
{
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
}
