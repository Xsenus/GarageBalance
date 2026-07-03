using GarageBalance.Api.Application.Releases;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace GarageBalance.Api.Tests.Releases;

public sealed class AppReleaseServiceTests
{
    [Fact]
    public async Task GetReleasesAsync_ReturnsSortedLimitedReleases()
    {
        using var directory = new TempContentRoot();
        directory.WriteReleasesJson(
            """
            [
              {
                "releaseId": "old",
                "version": "0.1.0",
                "publishedAt": "2026-06-22T10:00:00+07:00",
                "title": "Старый релиз",
                "summary": "Первое обновление.",
                "items": [{ "type": "new", "text": "Первый пункт." }]
              },
              {
                "releaseId": "new",
                "version": "0.2.0",
                "publishedAt": "2026-06-23T10:00:00+07:00",
                "title": "Новый релиз",
                "summary": "Второе обновление.",
                "items": [{ "type": "improved", "text": "Второй пункт." }]
              }
            ]
            """);
        var service = new AppReleaseService(directory.Environment);

        var result = await service.GetReleasesAsync(1, CancellationToken.None);

        Assert.True(result.Succeeded);
        var release = Assert.Single(result.Value!);
        Assert.Equal("new", release.ReleaseId);
        Assert.Equal("Новый релиз", release.Title);
        Assert.Equal("Второй пункт.", Assert.Single(release.Items).Text);
    }

    [Fact]
    public async Task GetReleasesAsync_ReturnsFailureForInvalidJson()
    {
        using var directory = new TempContentRoot();
        directory.WriteReleasesJson("{ invalid");
        var service = new AppReleaseService(directory.Environment);

        var result = await service.GetReleasesAsync(null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("releases_file_invalid", result.ErrorCode);
    }

    [Fact]
    public async Task GetReleasesAsync_ReturnsFailureForMissingFile()
    {
        using var directory = new TempContentRoot();
        var service = new AppReleaseService(directory.Environment);

        var result = await service.GetReleasesAsync(null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("releases_file_missing", result.ErrorCode);
    }

    [Theory]
    [InlineData("GET /api/import/access/runs/{id}/report")]
    [InlineData("GET /api/reports/consolidated/export/xlsx")]
    [InlineData("GET /api/reports/consolidated/export/pdf")]
    [InlineData("GET /api/reports/income/export/xlsx")]
    [InlineData("GET /api/reports/income/export/pdf")]
    [InlineData("GET /api/reports/expense/export/xlsx")]
    [InlineData("GET /api/reports/expense/export/pdf")]
    public void ReleaseNotes_DoNotDescribeAuditWritingExportsAsGet(string staleEndpoint)
    {
        var releasesJson = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api", "AppReleases", "releases.json"));

        Assert.DoesNotContain(staleEndpoint, releasesJson, StringComparison.Ordinal);
    }

    private sealed class TempContentRoot : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"garagebalance-releases-{Guid.NewGuid():N}");

        public TempContentRoot()
        {
            Directory.CreateDirectory(_path);
            Environment = new FakeWebHostEnvironment(_path);
        }

        public IWebHostEnvironment Environment { get; }

        public void WriteReleasesJson(string json)
        {
            var releasesDirectory = Path.Combine(_path, "AppReleases");
            Directory.CreateDirectory(releasesDirectory);
            File.WriteAllText(Path.Combine(releasesDirectory, "releases.json"), json);
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FakeWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GarageBalance.Api.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);

        public string ContentRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = "Development";

        public string WebRootPath { get; set; } = contentRootPath;

        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
