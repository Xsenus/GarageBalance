using GarageBalance.Api.Application.Backups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class DatabaseStartupOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; init; }
    public bool RequirePreMigrationBackup { get; init; } = true;
}

public sealed class DatabaseStartupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseStartupOptions> options,
    ILogger<DatabaseStartupHostedService> logger) : IHostedService
{
    private readonly DatabaseStartupOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Database migrations on startup are disabled by configuration.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GarageBalanceDbContext>();
        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        if (_options.RequirePreMigrationBackup)
        {
            var backupService = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            var backup = await backupService.CreateAsync(
                DatabaseBackupKind.PreUpdate,
                "Резервная копия перед автоматическим применением миграций.",
                actorUserId: null,
                cancellationToken);
            if (!backup.Succeeded)
            {
                throw new InvalidOperationException($"Pre-migration backup failed: {backup.ErrorCode}.");
            }
        }

        logger.LogInformation("Applying {MigrationCount} pending database migrations.", pendingMigrations.Length);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
