using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Integrations;

public sealed class OneCFreshSyncBackgroundOptions
{
    public const string SectionName = "Integrations:OneCFreshBackground";

    [Range(1, 32)]
    public int Capacity { get; init; } = 8;

    [Range(5, 300)]
    public int AdapterTimeoutSeconds { get; init; } = 30;
}

public sealed record OneCFreshSyncBackgroundJob(
    OneCFreshSyncRequest Request,
    Guid? ActorUserId,
    bool IsRetry);

public interface IOneCFreshSyncBackgroundQueue
{
    bool TryQueue(OneCFreshSyncBackgroundJob job);
    ValueTask<OneCFreshSyncBackgroundJob> DequeueAsync(CancellationToken cancellationToken);
}

public sealed class OneCFreshSyncBackgroundQueue : IOneCFreshSyncBackgroundQueue
{
    private readonly Channel<OneCFreshSyncBackgroundJob> _channel;

    public OneCFreshSyncBackgroundQueue(IOptions<OneCFreshSyncBackgroundOptions> options)
    {
        _channel = Channel.CreateBounded<OneCFreshSyncBackgroundJob>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryQueue(OneCFreshSyncBackgroundJob job) => _channel.Writer.TryWrite(job);

    public ValueTask<OneCFreshSyncBackgroundJob> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class OneCFreshSyncBackgroundWorker(
    IServiceScopeFactory scopeFactory,
    IOneCFreshSyncBackgroundQueue queue,
    ILogger<OneCFreshSyncBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            OneCFreshSyncBackgroundJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OneCFreshSyncService>();
                await service.ExecuteQueuedSyncAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "1C Fresh background synchronization failed. ExceptionType={ExceptionType}",
                    exception.GetType().Name);
            }
        }
    }
}
