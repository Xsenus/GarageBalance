using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        if (dbContext.Database.IsNpgsql())
        {
            return await GetPostgresListAsync(
                normalizedSearch,
                includeArchived,
                isRegular,
                isMetered,
                limit,
                businessDate,
                cancellationToken);
        }

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

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetPostgresListAsync(
        string? normalizedSearch,
        bool includeArchived,
        bool? isRegular,
        bool? isMetered,
        int limit,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var archiveClause = includeArchived ? string.Empty : "AND setting.\"IsArchived\" = FALSE";
        var regularClause = isRegular.HasValue ? "AND setting.\"IsRegular\" = @is_regular" : string.Empty;
        var meteredClause = isMetered.HasValue ? "AND setting.\"IsMetered\" = @is_metered" : string.Empty;
        var searchClause = normalizedSearch is null
            ? string.Empty
            : "AND setting.\"Name\" ILIKE @search COLLATE \"und-x-icu\" ESCAPE '\\'";
        var sql = $$"""
            SELECT setting."Id" AS "Id",
                   setting."Name" AS "Name",
                   setting."IsRegular" AS "IsRegular",
                   setting."PeriodicityMonths" AS "PeriodicityMonths",
                   setting."AccrualStartMonth" AS "AccrualStartMonth",
                   setting."PaymentDueDay" AS "PaymentDueDay",
                   setting."PaymentDueMonth" AS "PaymentDueMonth",
                   setting."OverdueGraceDays" AS "OverdueGraceDays",
                   setting."IncomeTypeId" AS "IncomeTypeId",
                   setting."TariffId" AS "StoredTariffId",
                   setting."IsMetered" AS "IsMetered",
                   setting."MeterKind" AS "MeterKind",
                   setting."HasTieredTariff" AS "HasTieredTariff",
                   setting."UnitName" AS "UnitName",
                   setting."IsArchived" AS "IsArchived",
                   setting."Version" AS "Version",
                   direct_tariff."Id" AS "DirectTariffId",
                   direct_tariff."CalculationBase" AS "DirectTariffCalculationBase",
                   direct_tariff."EffectiveFrom" AS "DirectTariffEffectiveFrom",
                   EXISTS (
                       SELECT 1
                       FROM charge_service_tariff_versions existing_version
                       INNER JOIN tariffs existing_tariff ON existing_tariff."Id" = existing_version."TariffId"
                       WHERE existing_version."ChargeServiceSettingId" = setting."Id"
                         AND existing_version."IsArchived" = FALSE
                         AND existing_tariff."IsArchived" = FALSE
                   ) AS "HasTariffVersions",
                   active_version.tariff_id AS "ActiveTariffId",
                   active_version.effective_from AS "ActiveTariffEffectiveFrom",
                   active_version.effective_to AS "ActiveTariffEffectiveTo",
                   active_version.calculation_base AS "ActiveTariffCalculationBase",
                   active_version.electricity_first_rate AS "ActiveTariffElectricityFirstRate",
                   active_version.electricity_second_rate AS "ActiveTariffElectricitySecondRate",
                   active_version.electricity_tiers_json AS "ActiveTariffElectricityTiersJson"
            FROM charge_service_settings setting
            LEFT JOIN tariffs direct_tariff ON direct_tariff."Id" = setting."TariffId"
            LEFT JOIN LATERAL (
                SELECT version."TariffId" AS tariff_id,
                       version."EffectiveFrom" AS effective_from,
                       version."EffectiveTo" AS effective_to,
                       tariff."CalculationBase" AS calculation_base,
                       tariff."ElectricityFirstRate" AS electricity_first_rate,
                       tariff."ElectricitySecondRate" AS electricity_second_rate,
                       tariff."ElectricityTiersJson" AS electricity_tiers_json
                FROM charge_service_tariff_versions version
                INNER JOIN tariffs tariff ON tariff."Id" = version."TariffId"
                WHERE version."ChargeServiceSettingId" = setting."Id"
                  AND version."IsArchived" = FALSE
                  AND tariff."IsArchived" = FALSE
                  AND version."EffectiveFrom" <= @business_date::date
                  AND (version."EffectiveTo" IS NULL OR version."EffectiveTo" >= @business_date::date)
                ORDER BY version."EffectiveFrom" DESC
                LIMIT 1
            ) active_version ON TRUE
            WHERE TRUE
              {{archiveClause}}
              {{regularClause}}
              {{meteredClause}}
              {{searchClause}}
            ORDER BY setting."Name", setting."Id"
            LIMIT @limit
            """;
        var parameters = new List<object>
        {
            new NpgsqlParameter<DateOnly>("business_date", businessDate),
            new NpgsqlParameter<int>("limit", limit)
        };
        if (isRegular.HasValue)
        {
            parameters.Add(new NpgsqlParameter<bool>("is_regular", isRegular.Value));
        }
        if (isMetered.HasValue)
        {
            parameters.Add(new NpgsqlParameter<bool>("is_metered", isMetered.Value));
        }
        if (normalizedSearch is not null)
        {
            parameters.Add(new NpgsqlParameter<string>(
                "search",
                PostgresLikeSearch.ContainsPattern(normalizedSearch)));
        }

        var rows = await dbContext.Database
            .SqlQueryRaw<ChargeServiceListRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);
        return rows.Select(row => CreateCompactSetting(row, businessDate)).ToList();
    }

    private static ChargeServiceSetting CreateCompactSetting(
        ChargeServiceListRow row,
        DateOnly businessDate)
    {
        var directTariff = row.DirectTariffId.HasValue
            ? CreateCompactTariff(
                row.DirectTariffId.Value,
                row.DirectTariffCalculationBase!,
                row.DirectTariffEffectiveFrom!.Value)
            : null;
        var setting = new ChargeServiceSetting
        {
            Id = row.Id,
            Name = row.Name,
            IsRegular = row.IsRegular,
            PeriodicityMonths = row.PeriodicityMonths,
            AccrualStartMonth = row.AccrualStartMonth,
            PaymentDueDay = row.PaymentDueDay,
            PaymentDueMonth = row.PaymentDueMonth,
            OverdueGraceDays = row.OverdueGraceDays,
            IncomeTypeId = row.IncomeTypeId,
            TariffId = row.StoredTariffId,
            Tariff = directTariff,
            IsMetered = row.IsMetered,
            MeterKind = row.MeterKind,
            HasTieredTariff = row.HasTieredTariff,
            UnitName = row.UnitName,
            IsArchived = row.IsArchived,
            Version = row.Version
        };

        if (row.ActiveTariffId.HasValue)
        {
            var activeTariff = CreateCompactTariff(
                row.ActiveTariffId.Value,
                row.ActiveTariffCalculationBase!,
                row.ActiveTariffEffectiveFrom!.Value,
                row.ActiveTariffElectricityFirstRate,
                row.ActiveTariffElectricitySecondRate,
                row.ActiveTariffElectricityTiersJson);
            setting.TariffVersions.Add(new ChargeServiceTariffVersion
            {
                ChargeServiceSettingId = setting.Id,
                ChargeServiceSetting = setting,
                TariffId = activeTariff.Id,
                Tariff = activeTariff,
                EffectiveFrom = row.ActiveTariffEffectiveFrom.Value,
                EffectiveTo = row.ActiveTariffEffectiveTo
            });
            var selectedHistoricalVersion = setting.TariffId != activeTariff.Id;
            setting.TariffId = activeTariff.Id;
            setting.Tariff = activeTariff;
            if (selectedHistoricalVersion)
            {
                ApplyTariffMode(setting, activeTariff);
            }
        }
        else if (row.HasTariffVersions || directTariff?.EffectiveFrom > businessDate)
        {
            setting.TariffId = null;
            setting.Tariff = null;
        }

        return setting;
    }

    private static Tariff CreateCompactTariff(
        Guid id,
        string calculationBase,
        DateOnly effectiveFrom,
        decimal? electricityFirstRate = null,
        decimal? electricitySecondRate = null,
        string? electricityTiersJson = null) =>
        new()
        {
            Id = id,
            Name = string.Empty,
            CalculationBase = calculationBase,
            EffectiveFrom = effectiveFrom,
            ElectricityFirstRate = electricityFirstRate,
            ElectricitySecondRate = electricitySecondRate,
            ElectricityTiersJson = electricityTiersJson
        };

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

    private sealed record ChargeServiceListRow(
        Guid Id,
        string Name,
        bool IsRegular,
        int? PeriodicityMonths,
        int? AccrualStartMonth,
        int? PaymentDueDay,
        int? PaymentDueMonth,
        int OverdueGraceDays,
        Guid? IncomeTypeId,
        Guid? StoredTariffId,
        bool IsMetered,
        string? MeterKind,
        bool HasTieredTariff,
        string? UnitName,
        bool IsArchived,
        Guid Version,
        Guid? DirectTariffId,
        string? DirectTariffCalculationBase,
        DateOnly? DirectTariffEffectiveFrom,
        bool HasTariffVersions,
        Guid? ActiveTariffId,
        DateOnly? ActiveTariffEffectiveFrom,
        DateOnly? ActiveTariffEffectiveTo,
        string? ActiveTariffCalculationBase,
        decimal? ActiveTariffElectricityFirstRate,
        decimal? ActiveTariffElectricitySecondRate,
        string? ActiveTariffElectricityTiersJson);
}
