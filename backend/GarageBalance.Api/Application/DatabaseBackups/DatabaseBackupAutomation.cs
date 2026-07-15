using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Backups;

public sealed class DatabaseBackupAutomationRunner(
    IDatabaseBackupService backupService,
    IOptions<DatabaseBackupOptions> options,
    TimeProvider timeProvider,
    ILogger<DatabaseBackupAutomationRunner> logger)
{
    private readonly DatabaseBackupOptions _options = options.Value;

    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.AutomaticEnabled)
        {
            return false;
        }

        var status = await backupService.GetStatusAsync(cancellationToken);
        var lastAutomatic = status.Backups
            .Where(backup => backup.Kind == "automatic")
            .MaxBy(backup => backup.CreatedAtUtc);
        if (lastAutomatic is not null && timeProvider.GetUtcNow() - lastAutomatic.CreatedAtUtc < TimeSpan.FromHours(_options.IntervalHours))
        {
            return false;
        }

        var result = await backupService.CreateAsync(
            DatabaseBackupKind.Automatic,
            "Автоматическая резервная копия по расписанию.",
            actorUserId: null,
            cancellationToken);
        if (!result.Succeeded)
        {
            logger.LogWarning("Automatic database backup did not complete: {BackupErrorCode}.", result.ErrorCode);
            return false;
        }

        return true;
    }
}

public sealed class DatabaseBackupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseBackupOptions> options,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    private readonly DatabaseBackupOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.AutomaticEnabled)
        {
            logger.LogInformation("Automatic database backups are disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<DatabaseBackupAutomationRunner>();
                await runner.RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Automatic database backup check failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }

            try
            {
                var checkInterval = TimeSpan.FromHours(Math.Min(_options.IntervalHours, 1));
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
