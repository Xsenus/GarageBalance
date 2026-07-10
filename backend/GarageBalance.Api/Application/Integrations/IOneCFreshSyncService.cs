namespace GarageBalance.Api.Application.Integrations;

public interface IOneCFreshSyncService
{
    Task<OneCFreshSyncResult<OneCFreshSyncPreviewDto>> PreviewSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<OneCFreshSyncResult<OneCFreshSyncDto>> StartSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<OneCFreshSyncResult<OneCFreshSyncDto>> RetrySyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
