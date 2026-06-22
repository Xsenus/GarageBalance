namespace GarageBalance.Api.Application.Releases;

public interface IAppReleaseService
{
    Task<AppReleaseResult<IReadOnlyList<AppReleaseDto>>> GetReleasesAsync(int? limit, CancellationToken cancellationToken);
}
