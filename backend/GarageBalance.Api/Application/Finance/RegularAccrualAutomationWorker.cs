using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Finance;

public sealed class RegularAccrualAutomationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RegularAccrualAutomationOptions> options,
    ILogger<RegularAccrualAutomationWorker> logger) : BackgroundService
{
    private readonly RegularAccrualAutomationOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Regular accrual automation is disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.CheckIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<RegularAccrualAutomationRunner>();
                await runner.RunCurrentMonthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Regular accrual automation failed and will be retried on the next scheduled check.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
