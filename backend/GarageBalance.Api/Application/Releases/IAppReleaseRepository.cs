namespace GarageBalance.Api.Application.Releases;

public interface IAppReleaseRepository
{
    Task<AppReleasePageDto> GetPageAsync(bool includeDrafts, int offset, int limit, CancellationToken cancellationToken);

    Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken);
}
