using GarageBalance.Api.Application.Releases;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarageBalance.Api.Tests.Releases;

public sealed class AppReleaseCatalogSynchronizerTests
{
    [Fact]
    public async Task SynchronizeOnceAsync_ImportsReleaseSourceIntoDatabase()
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
            await synchronizer.SynchronizeOnceAsync(CancellationToken.None);

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

    [Fact]
    public async Task StartAsync_DoesNotWaitForDatabaseSynchronization()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"garagebalance-release-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "AppReleases"));
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "AppReleases", "releases.json"),
            "[]");

        var allowSynchronizationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IAppReleaseRepository>(
                new BlockingAppReleaseRepository(allowSynchronizationToFinish));
            await using var provider = services.BuildServiceProvider();
            var synchronizer = new AppReleaseCatalogSynchronizer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeWebHostEnvironment(rootPath),
                NullLogger<AppReleaseCatalogSynchronizer>.Instance);

            var startTask = synchronizer.StartAsync(CancellationToken.None);

            Assert.True(startTask.IsCompletedSuccessfully);

            allowSynchronizationToFinish.SetResult();
            await synchronizer.StopAsync(CancellationToken.None);
        }
        finally
        {
            allowSynchronizationToFinish.TrySetResult();
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SynchronizeOnceAsync_LogsFailureWithoutPropagatingIt()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"garagebalance-release-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "AppReleases"));
        await File.WriteAllTextAsync(
            Path.Combine(rootPath, "AppReleases", "releases.json"),
            "[]");

        try
        {
            var logger = new CapturingLogger();
            var services = new ServiceCollection();
            services.AddSingleton<IAppReleaseRepository>(new FailingAppReleaseRepository());
            await using var provider = services.BuildServiceProvider();
            var synchronizer = new AppReleaseCatalogSynchronizer(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeWebHostEnvironment(rootPath),
                logger);

            await synchronizer.SynchronizeOnceAsync(CancellationToken.None);

            Assert.True(logger.ErrorLogged.Task.IsCompletedSuccessfully);
            Assert.NotNull(logger.Exception);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class BlockingAppReleaseRepository(
        TaskCompletionSource allowSynchronizationToFinish) : IAppReleaseRepository
    {
        public Task<AppReleasePageDto> GetPageAsync(bool includeDrafts, int offset, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken)
        {
            await allowSynchronizationToFinish.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailingAppReleaseRepository : IAppReleaseRepository
    {
        public Task<AppReleasePageDto> GetPageAsync(bool includeDrafts, int offset, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected synchronization failure.");
    }

    private sealed class CapturingLogger : ILogger<AppReleaseCatalogSynchronizer>
    {
        public TaskCompletionSource ErrorLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Exception { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Error)
            {
                return;
            }

            Exception = exception;
            ErrorLogged.TrySetResult();
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
