namespace GarageBalance.Api.Application.Releases;

public interface IAppReleaseService
{
    Task<AppReleaseResult<AppReleasePageDto>> GetReleasesAsync(int? offset, int? limit, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleasePageDto>> GetManageableReleasesAsync(int? offset, int? limit, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> CreateReleaseAsync(UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> UpdateReleaseAsync(string releaseId, UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> PublishReleaseAsync(string releaseId, Guid? actorUserId, CancellationToken cancellationToken);
}
