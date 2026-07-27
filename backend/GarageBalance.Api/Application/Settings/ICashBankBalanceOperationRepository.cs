using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Settings;

public interface ICashBankBalanceOperationRepository
{
    void Add(CashBankBalanceOperation operation);

    Task<CashBankBalanceOperationTotals> GetTotalsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CashBankBalanceOperation>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken);
}

public sealed record CashBankBalanceOperationTotals(
    decimal CashOpeningBalance,
    decimal BankOpeningBalance,
    decimal CashNetAdjustment,
    decimal BankNetAdjustment);
