using GarageBalance.Api.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Backups;

public sealed class DatabaseStartupHostedServiceTests
{
    [Fact]
    public async Task StartAndStop_DoNotCreateDatabaseScopeWhenStartupMigrationsAreDisabled()
    {
        var scopeFactory = new ThrowingScopeFactory();
        var service = new DatabaseStartupHostedService(
            scopeFactory,
            Options.Create(new DatabaseStartupOptions { ApplyMigrationsOnStartup = false }),
            NullLogger<DatabaseStartupHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, scopeFactory.CreateScopeCount);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CreateScopeCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateScopeCount++;
            throw new InvalidOperationException("A database scope must not be created when startup migrations are disabled.");
        }
    }
}
