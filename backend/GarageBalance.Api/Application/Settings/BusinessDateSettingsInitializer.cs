namespace GarageBalance.Api.Application.Settings;

public sealed class BusinessDateSettingsInitializer(
    IServiceScopeFactory scopeFactory,
    IBusinessDateProvider businessDateProvider,
    ILogger<BusinessDateSettingsInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IApplicationSettingRepository>();
        var setting = await repository.FindAsync(ApplicationSettingsService.BusinessDateOverrideKey, cancellationToken);
        businessDateProvider.SetOverride(setting?.DateValue);
        logger.LogInformation(
            "Business date initialized. System date: {SystemDate}; override: {OverrideDate}.",
            businessDateProvider.SystemDate,
            businessDateProvider.OverrideDate);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
