namespace GarageBalance.Api.Application.Integrations;

public interface IOneCFreshSyncAdapter
{
    Task<OneCFreshSyncAdapterResult> StartAsync(OneCFreshSyncAdapterRequest request, CancellationToken cancellationToken);
}

public sealed record OneCFreshSyncAdapterRequest(
    string RefreshToken,
    string? Comment,
    DateTimeOffset RequestedAtUtc);

public sealed record OneCFreshSyncAdapterResult(
    string Status,
    string StatusMessage,
    string? ExternalRunId = null,
    string? ErrorCode = null)
{
    public static OneCFreshSyncAdapterResult Pending(string statusMessage) => new("pending_adapter", statusMessage);

    public static OneCFreshSyncAdapterResult Started(string statusMessage, string? externalRunId = null) => new("started", statusMessage, externalRunId);

    public static OneCFreshSyncAdapterResult Failed(string status, string statusMessage, string? errorCode = null) => new(status, statusMessage, null, errorCode);
}

public sealed class DisabledOneCFreshSyncAdapter : IOneCFreshSyncAdapter
{
    public Task<OneCFreshSyncAdapterResult> StartAsync(OneCFreshSyncAdapterRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(OneCFreshSyncAdapterResult.Pending(
            "Запуск синхронизации зарегистрирован в истории. Фактическая синхронизация будет доступна после подключения адаптера 1C Fresh."));
    }
}
