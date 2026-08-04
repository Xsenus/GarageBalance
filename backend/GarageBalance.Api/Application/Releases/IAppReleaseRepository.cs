namespace GarageBalance.Api.Application.Releases;

public interface IAppReleaseRepository
{
    Task<AppReleasePageDto> GetPageAsync(bool includeDrafts, int offset, int limit, CancellationToken cancellationToken);

    Task<AppReleaseDto?> FindAsync(string releaseId, CancellationToken cancellationToken);

    Task<bool> VersionExistsAsync(string version, string? excludedReleaseId, CancellationToken cancellationToken);

    Task StageUpsertAsync(AppReleaseDto release, CancellationToken cancellationToken);

    // Imports release entries that are not yet present. Existing database rows are
    // authoritative because an administrator may have edited them on a read-only deployment.
    Task SynchronizeAsync(IReadOnlyList<AppReleaseDto> releases, CancellationToken cancellationToken);
}
