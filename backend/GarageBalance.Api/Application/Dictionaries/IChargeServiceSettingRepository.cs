using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IChargeServiceSettingRepository
{
    Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(string? normalizedSearch, bool includeArchived, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularForDueDatesAsync(Guid incomeTypeId, Guid? tariffId, CancellationToken cancellationToken);
    Task<ChargeServiceSetting?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<ChargeServiceSetting?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task SetTariffVersionAsync(Guid serviceId, Guid tariffId, DateOnly effectiveFrom, CancellationToken cancellationToken);
    Task<bool> HasTariffVersionAsync(Guid tariffId, CancellationToken cancellationToken);
    void Add(ChargeServiceSetting setting);
}
