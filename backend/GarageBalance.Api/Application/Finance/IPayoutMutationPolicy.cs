using GarageBalance.Api.Application.Settings;

namespace GarageBalance.Api.Application.Finance;

public interface IPayoutMutationPolicy
{
    Task<PayoutMutationSettingsDto> GetAsync(CancellationToken cancellationToken);
}

public sealed class PayoutMutationPolicy(IApplicationSettingRepository repository) : IPayoutMutationPolicy
{
    public async Task<PayoutMutationSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await repository.FindAsync(
            ApplicationSettingsService.PayoutMutationActionsKey,
            cancellationToken);
        var mask = setting?.IntegerValue ?? 1;
        return new PayoutMutationSettingsDto(
            (mask & 1) != 0,
            (mask & 2) != 0,
            setting?.Version ?? Guid.Empty);
    }
}
