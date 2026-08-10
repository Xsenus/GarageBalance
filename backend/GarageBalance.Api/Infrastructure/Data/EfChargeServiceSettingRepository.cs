using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfChargeServiceSettingRepository(GarageBalanceDbContext dbContext) : IChargeServiceSettingRepository
{
    public async Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChargeServiceSettings
            .AsNoTracking()
            .Include(item => item.Tariff)
            .Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
        }

        return await query
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.ChargeServiceSettings.AsNoTracking()
            .Include(setting => setting.Tariff)
            .Where(setting => !setting.IsArchived && setting.IsRegular)
            .OrderBy(setting => setting.Name)
            .ToListAsync(cancellationToken);

        await ApplyTariffsForMonthAsync(settings, accountingMonth, cancellationToken);
        return settings;
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken) =>
        await GetActiveRegularMeteredCoreAsync(calculationBase, accountingMonth, limit, cancellationToken);

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredCoreAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.ChargeServiceSettings
            .Include(setting => setting.IncomeType)
            .Include(setting => setting.Tariff)
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeType != null &&
                !setting.IncomeType.IsArchived &&
                (setting.IsMetered ||
                 dbContext.ChargeServiceTariffVersions.Any(version =>
                     version.ChargeServiceSettingId == setting.Id && version.EffectiveFrom <= accountingMonth) &&
                 dbContext.ChargeServiceTariffVersions
                     .Where(version => version.ChargeServiceSettingId == setting.Id && version.EffectiveFrom <= accountingMonth)
                     .OrderByDescending(version => version.EffectiveFrom)
                     .Select(version => (Guid?)version.TariffId)
                     .FirstOrDefault() != setting.TariffId) &&
                (dbContext.ChargeServiceTariffVersions
                    .Where(version =>
                        version.ChargeServiceSettingId == setting.Id &&
                        version.EffectiveFrom <= accountingMonth &&
                        !version.Tariff.IsArchived)
                    .OrderByDescending(version => version.EffectiveFrom)
                    .Select(version => version.Tariff.CalculationBase)
                    .FirstOrDefault() == calculationBase ||
                 (!dbContext.ChargeServiceTariffVersions.Any(version => version.ChargeServiceSettingId == setting.Id) &&
                  setting.Tariff != null &&
                  !setting.Tariff.IsArchived &&
                  setting.Tariff.CalculationBase == calculationBase &&
                  setting.Tariff.EffectiveFrom <= accountingMonth)))
            .OrderBy(setting => setting.Name)
            .Take(limit)
            .ToListAsync(cancellationToken);

        await ApplyTariffsForMonthAsync(settings, accountingMonth, cancellationToken);
        return settings.Where(setting => setting.IsMetered && setting.Tariff?.CalculationBase == calculationBase).ToList();
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularForDueDatesAsync(
        Guid incomeTypeId,
        Guid? tariffId,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.ChargeServiceSettings.AsNoTracking()
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeTypeId == incomeTypeId &&
                (!tariffId.HasValue ||
                 setting.TariffId == tariffId.Value ||
                 dbContext.ChargeServiceTariffVersions.Any(version =>
                     version.ChargeServiceSettingId == setting.Id && version.TariffId == tariffId.Value)))
            .OrderBy(setting => setting.Name)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (!tariffId.HasValue || settings.All(setting => setting.TariffId == tariffId.Value))
        {
            return settings;
        }

        var historicalTariff = await dbContext.Tariffs.AsNoTracking()
            .SingleOrDefaultAsync(tariff => tariff.Id == tariffId.Value && !tariff.IsArchived, cancellationToken);
        if (historicalTariff is null)
        {
            return settings;
        }

        foreach (var setting in settings.Where(setting => setting.TariffId != tariffId.Value))
        {
            ApplyTariffMode(setting, historicalTariff);
        }

        return settings;
    }

    public Task<ChargeServiceSetting?> FindActiveAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && !item.IsArchived, cancellationToken);

    public Task<ChargeServiceSetting?> FindArchivedAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.SingleOrDefaultAsync(item => item.Id == id && item.IsArchived, cancellationToken);

    public Task<bool> ActiveDuplicateExistsAsync(Guid? ignoredId, string name, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceSettings.AsNoTracking().AnyAsync(
            item => !item.IsArchived && item.Name == name && (!ignoredId.HasValue || item.Id != ignoredId.Value),
            cancellationToken);

    public Task<Tariff?> FindTariffVersionAsync(
        Guid serviceId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken) =>
        dbContext.ChargeServiceTariffVersions
            .Where(item => item.ChargeServiceSettingId == serviceId && item.EffectiveFrom == effectiveFrom)
            .Select(item => item.Tariff)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SetTariffVersionAsync(
        Guid serviceId,
        Guid tariffId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ChargeServiceTariffVersions.SingleOrDefaultAsync(
            item => item.ChargeServiceSettingId == serviceId && item.EffectiveFrom == effectiveFrom,
            cancellationToken);
        if (existing is null)
        {
            dbContext.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = serviceId,
                TariffId = tariffId,
                EffectiveFrom = effectiveFrom
            });
            return;
        }

        existing.TariffId = tariffId;
    }

    public Task<bool> HasTariffVersionAsync(Guid tariffId, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceTariffVersions.AsNoTracking()
            .AnyAsync(item => item.TariffId == tariffId, cancellationToken);

    public void Add(ChargeServiceSetting setting) => dbContext.ChargeServiceSettings.Add(setting);

    private async Task ApplyTariffsForMonthAsync(
        IReadOnlyCollection<ChargeServiceSetting> settings,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        if (settings.Count == 0)
        {
            return;
        }

        var serviceIds = settings.Select(setting => setting.Id).ToArray();
        var versions = await dbContext.ChargeServiceTariffVersions
            .Include(version => version.Tariff)
            .Where(version =>
                serviceIds.Contains(version.ChargeServiceSettingId) &&
                version.EffectiveFrom <= accountingMonth &&
                !version.Tariff.IsArchived)
            .OrderByDescending(version => version.EffectiveFrom)
            .ToListAsync(cancellationToken);
        var applicable = versions
            .GroupBy(version => version.ChargeServiceSettingId)
            .ToDictionary(group => group.Key, group => group.First().Tariff);

        foreach (var setting in settings)
        {
            if (applicable.TryGetValue(setting.Id, out var tariff))
            {
                var selectedHistoricalVersion = setting.TariffId != tariff.Id;
                setting.TariffId = tariff.Id;
                setting.Tariff = tariff;
                if (selectedHistoricalVersion)
                {
                    ApplyTariffMode(setting, tariff);
                }
            }
            else if (setting.Tariff?.EffectiveFrom > accountingMonth)
            {
                setting.TariffId = null;
                setting.Tariff = null;
            }
        }
    }

    private static void ApplyTariffMode(ChargeServiceSetting setting, Tariff tariff)
    {
        setting.IsMetered = tariff.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;
        setting.HasTieredTariff = setting.IsMetered &&
            (!string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson) ||
             tariff.ElectricityFirstRate.HasValue && tariff.ElectricitySecondRate.HasValue);
        setting.UnitName = TariffCalculationBases.GetUnitName(tariff.CalculationBase);
    }
}
