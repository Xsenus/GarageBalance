using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfApplicationSettingRepository(GarageBalanceDbContext dbContext) : IApplicationSettingRepository
{
    public Task<ApplicationSetting?> FindAsync(string key, CancellationToken cancellationToken) =>
        dbContext.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken);

    public Task<ApplicationSetting?> FindForUpdateAsync(string key, CancellationToken cancellationToken) =>
        dbContext.ApplicationSettings.SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken);

    public void Add(ApplicationSetting setting) => dbContext.ApplicationSettings.Add(setting);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
