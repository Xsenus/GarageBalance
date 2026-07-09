namespace GarageBalance.Api.Application.Integrations;

public interface IOneCFreshSyncService
{
    Task<OneCFreshSyncResult<OneCFreshSyncDto>> StartSyncAsync(
        OneCFreshSyncRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
