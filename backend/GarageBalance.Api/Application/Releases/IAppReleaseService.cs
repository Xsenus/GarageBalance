namespace GarageBalance.Api.Application.Releases;

public interface IAppReleaseService
{
    Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken);

    Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetManageableReleasesAsync(int? limit, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> CreateReleaseAsync(UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> UpdateReleaseAsync(string releaseId, UpsertAppReleaseRequest request, Guid? actorUserId, CancellationToken cancellationToken);

    Task<AppReleaseResult<AppReleaseDto>> PublishReleaseAsync(string releaseId, Guid? actorUserId, CancellationToken cancellationToken);
}
