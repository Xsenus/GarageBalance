using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseCatalogSynchronizer(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ILogger<AppReleaseCatalogSynchronizer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not delay Kestrel startup and deployment health checks while the
        // release catalog is being deserialized and synchronized with PostgreSQL.
        await Task.Yield();

        await SynchronizeOnceAsync(stoppingToken);
    }

    internal async Task SynchronizeOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            var path = Path.Combine(environment.ContentRootPath, "AppReleases", "releases.json");
            if (!File.Exists(path))
            {
                logger.LogWarning("App release source file was not found at {Path}.", path);
                return;
            }

            await using var stream = File.OpenRead(path);
            var releases = await JsonSerializer.DeserializeAsync<List<AppReleaseDto>>(stream, JsonOptions, stoppingToken) ?? [];
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAppReleaseRepository>();
            await repository.SynchronizeAsync(releases, stoppingToken);
            logger.LogInformation("Synchronized {Count} app releases with the database.", releases.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("App release catalog synchronization was cancelled during shutdown.");
        }
        catch (Exception exception)
        {
            // Release notes are auxiliary content and must not make the whole
            // accounting application unavailable when their synchronization fails.
            logger.LogError(exception, "App release catalog synchronization failed.");
        }
    }
}
