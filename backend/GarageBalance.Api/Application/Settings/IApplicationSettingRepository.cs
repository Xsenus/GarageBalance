using GarageBalance.Api.Domain.Settings;

namespace GarageBalance.Api.Application.Settings;

public interface IApplicationSettingRepository
{
    Task<ApplicationSetting?> FindAsync(string key, CancellationToken cancellationToken);
    Task<ApplicationSetting?> FindForUpdateAsync(string key, CancellationToken cancellationToken);
    void Add(ApplicationSetting setting);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
