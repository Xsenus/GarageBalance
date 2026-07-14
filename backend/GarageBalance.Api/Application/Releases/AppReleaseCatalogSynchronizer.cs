using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GarageBalance.Api.Application.Releases;

public sealed class AppReleaseCatalogSynchronizer(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ILogger<AppReleaseCatalogSynchronizer> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(environment.ContentRootPath, "AppReleases", "releases.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("App release source file was not found at {Path}.", path);
            return;
        }

        await using var stream = File.OpenRead(path);
        var releases = await JsonSerializer.DeserializeAsync<List<AppReleaseDto>>(stream, JsonOptions, cancellationToken) ?? [];
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAppReleaseRepository>();
        await repository.SynchronizeAsync(releases, cancellationToken);
        logger.LogInformation("Synchronized {Count} app releases with the database.", releases.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
