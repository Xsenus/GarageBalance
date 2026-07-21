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

        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = false;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IRegularAccrualAutomationRunner>();
                await runner.RunCurrentMonthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failed = true;
                logger.LogError(
                    exception,
                    "Regular accrual automation failed and will be retried in {RetryMinutes} minutes.",
                    _options.FailureRetryMinutes);
            }

            try
            {
                await Task.Delay(_options.GetDelayAfterRun(failed), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
