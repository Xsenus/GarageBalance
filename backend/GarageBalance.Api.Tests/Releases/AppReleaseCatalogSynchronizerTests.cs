using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Releases;

public sealed class AppReleaseCatalogSynchronizerTests
{
    [Fact]
    public async Task StartAsync_ImportsReleaseSourceIntoDatabase()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"garagebalance-release-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "AppReleases"));
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "AppReleases", "releases.json"),
            """
            [{
              "releaseId": "release-1",
              "version": "0.1.0",
              "publishedAt": "2026-07-14T10:00:00+07:00",
              "title": "Новое обновление",
              "summary": "Описание.",
              "items": [{ "type": "improved", "text": "Изменение." }],
              "isPublished": true
            }]
            """);

        try
        {
            await using var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<GarageBalanceDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<IAppReleaseRepository, EfAppReleaseRepository>();
            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
                await context.Database.EnsureCreatedAsync();
            }

            var synchronizer = new AppReleaseCatalogSynchronizer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeWebHostEnvironment(rootPath),
                NullLogger<AppReleaseCatalogSynchronizer>.Instance);
            await synchronizer.StartAsync(CancellationToken.None);
            await synchronizer.StartAsync(CancellationToken.None);

            using var verificationScope = provider.CreateScope();
            var repository = verificationScope.ServiceProvider.GetRequiredService<IAppReleaseRepository>();
            var page = await repository.GetPageAsync(false, 0, 9, CancellationToken.None);
            Assert.Equal("release-1", Assert.Single(page.Items).ReleaseId);
            Assert.Equal(1, page.TotalCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
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
