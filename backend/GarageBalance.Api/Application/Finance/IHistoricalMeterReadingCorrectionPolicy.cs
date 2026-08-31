using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Finance;

public interface IHistoricalMeterReadingCorrectionPolicy
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

public sealed class HistoricalMeterReadingCorrectionPolicy(IApplicationSettingRepository repository)
    : IHistoricalMeterReadingCorrectionPolicy
{
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(
            ApplicationSettingsService.HistoricalMeterReadingCorrectionEnabledKey,
            cancellationToken);
        return setting?.BooleanValue == true;
    }
}
