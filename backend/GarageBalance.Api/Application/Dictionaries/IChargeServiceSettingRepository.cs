using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Dictionaries;

public interface IChargeServiceSettingRepository
{
    Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        bool? isRegular,
        bool? isMetered,
        int limit,
        DateOnly businessDate,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(DateOnly accountingMonth, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularForDueDatesAsync(Guid incomeTypeId, Guid? tariffId, CancellationToken cancellationToken);
    Task<ChargeServiceSetting?> FindActiveAsync(Guid id, CancellationToken cancellationToken);
    Task<ChargeServiceSetting?> FindArchivedAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken);
    Task<Tariff?> FindTariffVersionAsync(Guid serviceId, DateOnly effectiveFrom, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChargeServiceTariffVersion>> GetTariffPeriodsAsync(Guid serviceId, bool tracked, CancellationToken cancellationToken);
    Task SetTariffVersionAsync(Guid serviceId, Guid tariffId, DateOnly effectiveFrom, CancellationToken cancellationToken, DateOnly? effectiveTo = null);
    void ReplaceTariffPeriods(Guid serviceId, IReadOnlyCollection<ChargeServiceTariffVersion> existing, IReadOnlyCollection<ChargeServiceTariffVersion> replacements);
    Task<bool> HasTariffVersionAsync(Guid tariffId, CancellationToken cancellationToken);
    void Add(ChargeServiceSetting setting);
}
