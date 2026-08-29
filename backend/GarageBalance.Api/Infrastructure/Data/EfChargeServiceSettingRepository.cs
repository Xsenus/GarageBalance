using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfChargeServiceSettingRepository(GarageBalanceDbContext dbContext) : IChargeServiceSettingRepository
{
    public async Task<IReadOnlyList<ChargeServiceSetting>> GetListAsync(
        string? normalizedSearch,
        bool includeArchived,
        bool? isRegular,
        bool? isMetered,
        int limit,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChargeServiceSettings
            .AsNoTracking()
            .Include(item => item.Tariff)
            .Where(item => includeArchived || !item.IsArchived);
        if (normalizedSearch is not null)
        {
            if (dbContext.Database.IsNpgsql())
            {
                var pattern = PostgresLikeSearch.ContainsPattern(normalizedSearch);
                query = query.Where(item => EF.Functions.ILike(item.Name, pattern, @"\"));
            }
            else
            {
                query = query.Where(item => item.Name.ToLower().Contains(normalizedSearch));
            }
        }
        if (isRegular.HasValue)
        {
            query = query.Where(item => item.IsRegular == isRegular.Value);
        }
        if (isMetered.HasValue)
        {
            query = query.Where(item => item.IsMetered == isMetered.Value);
        }

        var rows = await query
            .Include(item => item.TariffVersions.Where(version =>
                !version.IsArchived &&
                version.EffectiveFrom <= businessDate &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= businessDate)))
                .ThenInclude(version => version.Tariff)
            .OrderBy(item => item.Name)
            .Take(limit)
            .Select(setting => new ChargeServiceSettingQueryRow(
                setting,
                setting.TariffVersions.Any(version => !version.IsArchived && !version.Tariff.IsArchived)))
            .ToListAsync(cancellationToken);
        var settings = rows.Select(row => row.Setting).ToList();
        var servicesWithVersions = rows
            .Where(row => row.HasTariffVersions)
            .Select(row => row.Setting.Id)
            .ToHashSet();

        await ApplyTariffsForMonthAsync(settings, businessDate, cancellationToken, servicesWithVersions);
        return settings;
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularAsync(
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var monthEnd = accountingMonth.AddMonths(1).AddDays(-1);
        var rows = await dbContext.ChargeServiceSettings.AsNoTracking()
            .Include(setting => setting.Tariff)
            .Include(setting => setting.TariffVersions.Where(version =>
                !version.IsArchived &&
                version.EffectiveFrom <= monthEnd &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= accountingMonth)))
                .ThenInclude(version => version.Tariff)
            .Where(setting => !setting.IsArchived && setting.IsRegular)
            .OrderBy(setting => setting.Name)
            .Select(setting => new ChargeServiceSettingQueryRow(
                setting,
                setting.TariffVersions.Any(version => !version.IsArchived && !version.Tariff.IsArchived)))
            .ToListAsync(cancellationToken);
        var settings = rows.Select(row => row.Setting).ToList();
        var servicesWithVersions = rows
            .Where(row => row.HasTariffVersions)
            .Select(row => row.Setting.Id)
            .ToHashSet();

        await ApplyTariffsForMonthAsync(settings, accountingMonth, cancellationToken, servicesWithVersions);
        return settings;
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken) =>
        await GetActiveRegularMeteredCoreAsync(null, accountingMonth, limit, cancellationToken);

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredAsync(
        string calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken) =>
        await GetActiveRegularMeteredCoreAsync(calculationBase, accountingMonth, limit, cancellationToken);

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularMeteredCoreAsync(
        string? calculationBase,
        DateOnly accountingMonth,
        int limit,
        CancellationToken cancellationToken)
    {
        var monthEnd = accountingMonth.AddMonths(1).AddDays(-1);
        var rows = await dbContext.ChargeServiceSettings
            .Include(setting => setting.IncomeType)
            .Include(setting => setting.Tariff)
            .Include(setting => setting.TariffVersions.Where(version =>
                !version.IsArchived &&
                version.EffectiveFrom <= monthEnd &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= accountingMonth)))
                .ThenInclude(version => version.Tariff)
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeType != null &&
                !setting.IncomeType.IsArchived &&
                (dbContext.ChargeServiceTariffVersions
                    .Where(version =>
                        version.ChargeServiceSettingId == setting.Id &&
                        !version.IsArchived &&
                        version.EffectiveFrom <= accountingMonth.AddMonths(1).AddDays(-1) &&
                        (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= accountingMonth) &&
                        !version.Tariff.IsArchived)
                    .OrderByDescending(version => version.EffectiveFrom)
                    .Select(version => version.Tariff.CalculationBase)
                    .Take(1)
                    .Any(activeCalculationBase => calculationBase == null
                        ? activeCalculationBase == TariffCalculationBases.MeterWater ||
                          activeCalculationBase == TariffCalculationBases.MeterElectricity
                        : activeCalculationBase == calculationBase) ||
                 !dbContext.ChargeServiceTariffVersions.Any(version => version.ChargeServiceSettingId == setting.Id) &&
                 setting.IsMetered &&
                 setting.Tariff != null &&
                 !setting.Tariff.IsArchived &&
                 setting.Tariff.EffectiveFrom <= accountingMonth &&
                 (calculationBase == null || setting.Tariff.CalculationBase == calculationBase)))
            .OrderBy(setting => setting.Name)
            .Take(limit)
            .Select(setting => new ChargeServiceSettingQueryRow(
                setting,
                setting.TariffVersions.Any(version => !version.IsArchived && !version.Tariff.IsArchived)))
            .ToListAsync(cancellationToken);
        var settings = rows.Select(row => row.Setting).ToList();
        var servicesWithVersions = rows
            .Where(row => row.HasTariffVersions)
            .Select(row => row.Setting.Id)
            .ToHashSet();

        await ApplyTariffsForMonthAsync(settings, accountingMonth, cancellationToken, servicesWithVersions);
        foreach (var setting in settings)
        {
            setting.MeterKind ??= setting.IncomeType?.Code switch
            {
                MeterKinds.Water => MeterKinds.Water,
                MeterKinds.Electricity => MeterKinds.Electricity,
                _ => MeterKinds.ForService(setting.Id)
            };
        }

        return settings.Where(setting =>
            setting.TariffVersions.Any(version =>
                !version.IsArchived &&
                !version.Tariff.IsArchived &&
                version.EffectiveFrom <= monthEnd &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= accountingMonth) &&
                version.Tariff.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity) ||
            setting.TariffVersions.Count == 0 && setting.IsMetered && setting.Tariff is not null).ToList();
    }

    public async Task<IReadOnlyList<ChargeServiceSetting>> GetActiveRegularForDueDatesAsync(
        Guid incomeTypeId,
        Guid? tariffId,
        DateOnly accountingMonth,
        CancellationToken cancellationToken)
    {
        var month = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var settings = await dbContext.ChargeServiceSettings.AsNoTracking()
            .Include(setting => setting.TariffVersions.Where(version =>
                !version.IsArchived &&
                !version.Tariff.IsArchived &&
                version.EffectiveFrom <= monthEnd &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= month)))
                .ThenInclude(version => version.Tariff)
            .Where(setting =>
                !setting.IsArchived &&
                setting.IsRegular &&
                setting.IncomeTypeId == incomeTypeId &&
                (!tariffId.HasValue ||
                 setting.TariffId == tariffId.Value ||
                 dbContext.ChargeServiceTariffVersions.Any(version =>
                     version.ChargeServiceSettingId == setting.Id && !version.IsArchived && version.TariffId == tariffId.Value)))
            .OrderBy(setting => setting.Name)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (!tariffId.HasValue || settings.All(setting => setting.TariffId == tariffId.Value))
        {
            return settings;
        }

        var historicalTariff = settings
            .SelectMany(setting => setting.TariffVersions)
            .Where(version => version.TariffId == tariffId.Value && !version.Tariff.IsArchived)
            .Select(version => version.Tariff)
            .FirstOrDefault()
            ?? await dbContext.Tariffs.AsNoTracking()
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

    public async Task<ChargeServiceTariffScheduleData> GetActiveTariffScheduleAsync(
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var setting = await dbContext.ChargeServiceSettings
            .AsNoTracking()
            .Where(item => item.Id == serviceId && !item.IsArchived)
            .Include(item => item.TariffVersions.Where(version => !version.IsArchived))
            .ThenInclude(version => version.Tariff)
            .SingleOrDefaultAsync(cancellationToken);

        return setting is null
            ? new ChargeServiceTariffScheduleData(false, [])
            : new ChargeServiceTariffScheduleData(
                true,
                setting.TariffVersions.OrderBy(item => item.EffectiveFrom).ToList());
    }

    public async Task SetTariffVersionAsync(
        Guid serviceId,
        Guid tariffId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken,
        DateOnly? effectiveTo = null)
    {
        var serviceVersions = dbContext.ChargeServiceTariffVersions
            .Where(item => item.ChargeServiceSettingId == serviceId);
        var activeVersions = serviceVersions.Where(item => !item.IsArchived);
        var selectedVersions = await activeVersions
            .Where(item => item.EffectiveFrom < effectiveFrom)
            .OrderByDescending(item => item.EffectiveFrom)
            .Take(1)
            .Concat(serviceVersions
                .Where(item => item.EffectiveFrom == effectiveFrom)
                .Take(1))
            .Concat(activeVersions
                .Where(item => item.EffectiveFrom > effectiveFrom)
                .OrderBy(item => item.EffectiveFrom)
                .Take(1))
            .ToListAsync(cancellationToken);
        var previous = selectedVersions.SingleOrDefault(item => item.EffectiveFrom < effectiveFrom);
        var existing = selectedVersions.SingleOrDefault(item => item.EffectiveFrom == effectiveFrom);
        var next = selectedVersions.SingleOrDefault(item => item.EffectiveFrom > effectiveFrom);
        if (previous is not null)
        {
            previous.EffectiveTo = effectiveFrom.AddDays(-1);
        }

        effectiveTo ??= next?.EffectiveFrom.AddDays(-1);
        if (existing is null)
        {
            dbContext.ChargeServiceTariffVersions.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = serviceId,
                TariffId = tariffId,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = effectiveTo
            });
            return;
        }

        existing.TariffId = tariffId;
        existing.EffectiveTo = effectiveTo;
        existing.IsArchived = false;
    }

    public async Task<IReadOnlyList<ChargeServiceTariffVersion>> GetTrackedTariffPeriodsAsync(
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChargeServiceTariffVersions
            .Include(item => item.Tariff)
            .Where(item => item.ChargeServiceSettingId == serviceId);

        return await query.OrderBy(item => item.EffectiveFrom).ToListAsync(cancellationToken);
    }

    public void ReplaceTariffPeriods(
        Guid serviceId,
        IReadOnlyCollection<ChargeServiceTariffVersion> existing,
        IReadOnlyCollection<ChargeServiceTariffVersion> replacements)
    {
        var requested = replacements.Where(item => item.ChargeServiceSettingId == serviceId).ToList();
        var requestedStarts = requested.Select(item => item.EffectiveFrom).ToHashSet();
        foreach (var obsolete in existing.Where(item => !requestedStarts.Contains(item.EffectiveFrom)))
        {
            obsolete.IsArchived = true;
        }
        foreach (var replacement in requested)
        {
            var current = existing.SingleOrDefault(item => item.EffectiveFrom == replacement.EffectiveFrom);
            if (current is null)
            {
                dbContext.ChargeServiceTariffVersions.Add(replacement);
                continue;
            }

            current.TariffId = replacement.TariffId;
            current.EffectiveTo = replacement.EffectiveTo;
            current.Tariff = replacement.Tariff;
            current.IsArchived = false;
        }
    }

    public Task<bool> HasTariffVersionAsync(Guid tariffId, CancellationToken cancellationToken) =>
        dbContext.ChargeServiceTariffVersions.AsNoTracking()
            .AnyAsync(item => item.TariffId == tariffId, cancellationToken);

    public void Add(ChargeServiceSetting setting) => dbContext.ChargeServiceSettings.Add(setting);

    private async Task ApplyTariffsForMonthAsync(
        IReadOnlyCollection<ChargeServiceSetting> settings,
        DateOnly accountingMonth,
        CancellationToken cancellationToken,
        IReadOnlySet<Guid>? loadedServiceIdsWithVersions = null)
    {
        if (settings.Count == 0)
        {
            return;
        }

        var versions = loadedServiceIdsWithVersions is not null
            ? settings
                .SelectMany(setting => setting.TariffVersions)
                .Where(version => !version.IsArchived && !version.Tariff.IsArchived)
                .OrderByDescending(version => version.EffectiveFrom)
                .ToList()
            : await LoadTariffVersionsAsync(settings, cancellationToken);
        var applicable = versions
            .Where(version =>
                version.EffectiveFrom <= accountingMonth &&
                (!version.EffectiveTo.HasValue || version.EffectiveTo.Value >= accountingMonth))
            .GroupBy(version => version.ChargeServiceSettingId)
            .ToDictionary(group => group.Key, group => group.First().Tariff);
        var servicesWithVersions = loadedServiceIdsWithVersions ??
            versions.Select(version => version.ChargeServiceSettingId).ToHashSet();

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
            else if (servicesWithVersions.Contains(setting.Id) || setting.Tariff?.EffectiveFrom > accountingMonth)
            {
                setting.TariffId = null;
                setting.Tariff = null;
            }
        }
    }

    private async Task<List<ChargeServiceTariffVersion>> LoadTariffVersionsAsync(
        IReadOnlyCollection<ChargeServiceSetting> settings,
        CancellationToken cancellationToken)
    {
        var serviceIds = settings.Select(setting => setting.Id).ToArray();
        return await dbContext.ChargeServiceTariffVersions
            .Include(version => version.Tariff)
            .Where(version =>
                serviceIds.Contains(version.ChargeServiceSettingId) &&
                !version.IsArchived &&
                !version.Tariff.IsArchived)
            .OrderByDescending(version => version.EffectiveFrom)
            .ToListAsync(cancellationToken);
    }

    private static void ApplyTariffMode(ChargeServiceSetting setting, Tariff tariff)
    {
        setting.IsMetered = tariff.CalculationBase is TariffCalculationBases.MeterWater or TariffCalculationBases.MeterElectricity;
        setting.HasTieredTariff = setting.IsMetered &&
            (!string.IsNullOrWhiteSpace(tariff.ElectricityTiersJson) ||
             tariff.ElectricityFirstRate.HasValue && tariff.ElectricitySecondRate.HasValue);
    }

    private sealed record ChargeServiceSettingQueryRow(
        ChargeServiceSetting Setting,
        bool HasTariffVersions);
}
