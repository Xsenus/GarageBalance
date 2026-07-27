using GarageBalance.Api.Application.Finance;

namespace GarageBalance.Api.Application.Settings;

public interface ICashBankBalanceSettingsService
{
    Task<CashBankBalanceSettingsDto> GetAsync(CancellationToken cancellationToken);

    Task<FinanceResult<CashBankBalanceSettingsDto>> UpdateOpeningBalancesAsync(
        UpdateCashBankOpeningBalancesRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<FinanceResult<CashBankBalanceSettingsDto>> CreateAdjustmentAsync(
        CreateCashBankBalanceAdjustmentRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken);
}
