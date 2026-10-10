using System.Globalization;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService : IGarageMeterBaselineService
{
    public async Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartValuesAsync(
        Garage garage,
        CancellationToken cancellationToken)
    {
        var settings = await GetStartValueSettingsAsync(garage, cancellationToken);
        var values = new List<GarageMeterStartValueDto>(settings.Count);
        foreach (var setting in settings)
        {
            var meterKind = setting.MeterKind!;
            var devices = (await meterReadingRepository.GetDevicesAsync(garage.Id, meterKind, cancellationToken))
                .OrderBy(device => device.InstalledOn)
                .ThenBy(device => device.Id)
                .ToList();
            var firstReading = await meterReadingRepository.GetFirstActiveAsync(garage.Id, meterKind, cancellationToken);
            var baseline = MeterBaselineResolver.FindBaselineDevice(devices, firstReading);
            values.Add(new GarageMeterStartValueDto(
                meterKind,
                setting.Name,
                setting.UnitName,
                baseline?.InitialValue ?? GetGarageStartValueColumn(garage, meterKind),
                firstReading is not null));
        }

        return values;
    }

    public async Task<IReadOnlyList<GarageMeterStartValueDto>> GetStartServicesForNewGarageAsync(
        CancellationToken cancellationToken)
    {
        var settings = await chargeServiceSettingRepository.GetListAsync(
            null, false, true, true, MaxAutomaticMeteredServices, businessDateProvider.Today, cancellationToken);
        return settings
            .Where(setting => setting is { IsRegular: true, IsMetered: true, IsArchived: false, AppliesToSelectedGarages: false } &&
                !string.IsNullOrWhiteSpace(setting.MeterKind) && MeterKinds.IsValid(setting.MeterKind))
            .OrderBy(setting => setting.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(setting => new GarageMeterStartValueDto(setting.MeterKind!, setting.Name, setting.UnitName, null, false))
            .ToList();
    }

    public async Task<FinanceResult<GarageMeterStartValueApplyResult>> ApplyStartValuesAsync(
        Garage garage,
        IReadOnlyList<UpsertGarageMeterStartValueRequest> requested,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var locks = new List<IAsyncDisposable>();
        var transferred = false;
        try
        {
            var kinds = requested.Select(item => item.MeterKind.Trim()).ToList();
            if (kinds.Count != kinds.Distinct(StringComparer.Ordinal).Count())
            {
                return StartValueFailure("meter_start_kind_duplicate", "Стартовое значение одного счётчика указано дважды.");
            }

            IReadOnlyList<ChargeServiceSetting>? settings = null;
            var changes = new List<GarageMeterStartValueChange>();
            foreach (var item in requested)
            {
                var meterKind = item.MeterKind.Trim();
                if (!MeterKinds.IsValid(meterKind))
                {
                    return StartValueFailure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
                }

                settings ??= await GetStartValueSettingsAsync(garage, cancellationToken);
                var setting = settings.FirstOrDefault(candidate => candidate.MeterKind == meterKind);
                var isLegacyKind = meterKind is MeterKinds.Water or MeterKinds.Electricity;
                if (setting is null && !isLegacyKind)
                {
                    return StartValueFailure(
                        "meter_start_service_not_applicable",
                        "Услуга со счётчиком не применяется к этому гаражу. Обновите карточку и повторите действие.");
                }

                locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                    [GetMeterChainLockKey(garage.Id, meterKind)],
                    cancellationToken));
                var label = setting?.Name ?? (meterKind == MeterKinds.Water ? "Вода" : "Электроэнергия");
                var step = await ApplyStartValueAsync(garage, meterKind, label, item.Value, actorUserId, locks, cancellationToken);
                if (!step.Succeeded)
                {
                    return FinanceResult<GarageMeterStartValueApplyResult>.Failure(step.ErrorCode!, step.ErrorMessage!);
                }

                if (step.Value is not null)
                {
                    changes.Add(step.Value);
                }
            }

            transferred = true;
            return FinanceResult<GarageMeterStartValueApplyResult>.Success(new GarageMeterStartValueApplyResult(changes, locks));
        }
        finally
        {
            if (!transferred)
            {
                foreach (var chainLock in Enumerable.Reverse(locks))
                {
                    await chainLock.DisposeAsync();
                }
            }
        }
    }

    private async Task<FinanceResult<GarageMeterStartValueChange?>> ApplyStartValueAsync(
        Garage garage,
        string meterKind,
        string label,
        decimal? requestedValue,
        Guid? actorUserId,
        List<IAsyncDisposable> locks,
        CancellationToken cancellationToken)
    {
        var devices = (await meterReadingRepository.GetDevicesForUpdateAsync(garage.Id, meterKind, cancellationToken)).ToList();
        var readings = (await meterReadingRepository.GetAllActiveForUpdateAsync(garage.Id, meterKind, cancellationToken)).ToList();
        var column = GetGarageStartValueColumn(garage, meterKind);
        var isLegacyKind = meterKind is MeterKinds.Water or MeterKinds.Electricity;
        var baseline = MeterBaselineResolver.FindBaselineDevice(devices, readings.FirstOrDefault());
        var oldValue = baseline?.InitialValue ?? column;

        if (!requestedValue.HasValue)
        {
            // Without devices and readings nothing depends on the value, so it may be cleared.
            if (baseline is not null || readings.Count > 0 || !column.HasValue || !isLegacyKind)
            {
                return FinanceResult<GarageMeterStartValueChange?>.Success(null);
            }

            SetGarageStartValueColumn(garage, meterKind, null);
            return FinanceResult<GarageMeterStartValueChange?>.Success(
                await AddStartValueChangeAsync(garage, meterKind, label, column, null, actorUserId));
        }

        var value = MoneyMath.RoundMeterValue(requestedValue.Value);
        if (isLegacyKind)
        {
            SetGarageStartValueColumn(garage, meterKind, value);
        }

        if (oldValue == value)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Success(null);
        }

        if (baseline is null && !isLegacyKind)
        {
            baseline = new MeterDevice
            {
                GarageId = garage.Id,
                Garage = garage,
                MeterKind = meterKind,
                SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial,
                InstalledOn = readings.FirstOrDefault()?.ReadingDate
                    ?? (garage.InitialMeterReadingMonth?.AddMonths(1) ?? MonthPeriod.Normalize(businessDateProvider.Today)),
                InitialValue = value
            };
            meterReadingRepository.Add(baseline);
            devices.Insert(0, baseline);
            foreach (var reading in readings.Where(reading => reading.MeterDeviceId is null))
            {
                reading.MeterDeviceId = baseline.Id;
                reading.MeterDevice = baseline;
            }
        }
        else if (baseline is not null)
        {
            if (baseline.FinalValue.HasValue && value > baseline.FinalValue.Value)
            {
                return FinanceResult<GarageMeterStartValueChange?>.Failure(
                    "meter_start_above_final",
                    $"Стартовое показание «{label}» не может быть больше конечного показания снятого счётчика ({FormatMeterValue(baseline.FinalValue.Value)}).");
            }

            baseline.InitialValue = value;
            baseline.Version = Guid.NewGuid();
            baseline.UpdatedAtUtc = timeProvider.GetUtcNow();
        }

        if (readings.Count > 0)
        {
            var rebuild = await RebuildChainAfterStartValueChangeAsync(
                garage, meterKind, label, value, baseline, devices, readings, actorUserId, locks, cancellationToken);
            if (!rebuild.Succeeded)
            {
                return FinanceResult<GarageMeterStartValueChange?>.Failure(rebuild.ErrorCode!, rebuild.ErrorMessage!);
            }
        }

        return FinanceResult<GarageMeterStartValueChange?>.Success(
            await AddStartValueChangeAsync(garage, meterKind, label, oldValue, value, actorUserId));
    }

    private async Task<FinanceResult<bool>> RebuildChainAfterStartValueChangeAsync(
        Garage garage,
        string meterKind,
        string label,
        decimal value,
        MeterDevice? baseline,
        List<MeterDevice> devices,
        List<MeterReading> readings,
        Guid? actorUserId,
        List<IAsyncDisposable> locks,
        CancellationToken cancellationToken)
    {
        if (baseline is not null)
        {
            var baselineIndex = devices.IndexOf(baseline);
            var successor = baselineIndex >= 0 && baselineIndex + 1 < devices.Count ? devices[baselineIndex + 1] : null;
            var replacement = successor is null
                ? null
                : readings.FirstOrDefault(reading => reading.IsMeterReplacement && reading.MeterDeviceId == successor.Id);
            if (replacement is not null && baseline.FinalValue.HasValue)
            {
                var lastOnBaseline = readings
                    .Where(reading => reading.Id != replacement.Id &&
                        reading.MeterDeviceId == baseline.Id &&
                        reading.AccountingMonth < replacement.AccountingMonth)
                    .OrderByDescending(reading => reading.AccountingMonth)
                    .FirstOrDefault();
                var consumed = MoneyMath.RoundMeterValue(
                    baseline.FinalValue.Value - (lastOnBaseline?.CurrentValue ?? baseline.InitialValue));
                if (consumed < 0)
                {
                    return FinanceResult<bool>.Failure(
                        "meter_start_above_final",
                        $"Стартовое показание «{label}» не может быть больше конечного показания снятого счётчика ({FormatMeterValue(baseline.FinalValue.Value)}).");
                }

                replacement.PreviousDeviceConsumption = consumed;
            }
        }

        var chainPlan = PlanMeterReadingChain(garage, meterKind, null, readings);
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<bool>.Failure(
                "meter_start_value_breaks_readings",
                $"Стартовое показание {FormatMeterValue(value)} («{label}») не подходит к уже внесённым показаниям. {chainPlan.ErrorMessage}");
        }

        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            garage.Id,
            meterKind,
            readings[0].AccountingMonth,
            chainPlan.Value!,
            missingReadingMonth: null,
            cancellationToken);
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Distinct()
            .ToArray();
        locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(allocationKeys, cancellationToken));
        if (await accrualPaymentAllocationRepository.HasActiveAllocationAsync(
            accrualRecalculations.Select(item => item.Accrual.Id).ToArray(),
            cancellationToken))
        {
            return FinanceResult<bool>.Failure(
                "meter_start_accrual_paid",
                $"Изменение стартового показания «{label}» пересчитывает полностью или частично оплаченное начисление. Сначала исправьте оплату или оформите отдельную корректировку.");
        }

        ApplyMeterReadingChainChanges(chainPlan.Value!, actorUserId, Guid.Empty);
        ApplyMeteredAccrualRecalculations(accrualRecalculations, actorUserId, "Пересчет после изменения стартового показания");
        if (allocationKeys.Length > 0)
        {
            await RebuildPaymentAllocationsAsync(
                allocationKeys,
                actorUserId,
                "Перераспределение после изменения стартового показания",
                garage.Id,
                cancellationToken);
        }

        return FinanceResult<bool>.Success(true);
    }

    private Task<GarageMeterStartValueChange> AddStartValueChangeAsync(
        Garage garage,
        string meterKind,
        string label,
        decimal? oldValue,
        decimal? newValue,
        Guid? actorUserId)
    {
        AddAudit(
            actorUserId,
            "finance.meter_start_value_changed",
            "garage",
            garage.Id,
            $"В гараже {garage.Number} изменено стартовое показание «{label}»: {FormatMeterValue(oldValue)} → {FormatMeterValue(newValue)}.",
            relatedGarageId: garage.Id.ToString(),
            relatedGarageNumber: garage.Number,
            metadata: new Dictionary<string, object?> { ["meterKind"] = meterKind },
            oldValues: new Dictionary<string, object?> { ["startValue"] = oldValue },
            newValues: new Dictionary<string, object?> { ["startValue"] = newValue });
        return Task.FromResult(new GarageMeterStartValueChange(meterKind, label, oldValue, newValue));
    }

    private async Task<IReadOnlyList<ChargeServiceSetting>> GetStartValueSettingsAsync(
        Garage garage,
        CancellationToken cancellationToken)
    {
        var settings = await chargeServiceSettingRepository.GetListAsync(
            null,
            false,
            true,
            true,
            MaxAutomaticMeteredServices,
            businessDateProvider.Today,
            cancellationToken);
        return settings
            .Where(setting => setting is { IsRegular: true, IsMetered: true, IsArchived: false } &&
                !string.IsNullOrWhiteSpace(setting.MeterKind) &&
                MeterKinds.IsValid(setting.MeterKind) &&
                setting.AppliesToGarage(garage.Id))
            .OrderBy(setting => setting.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static decimal? GetGarageStartValueColumn(Garage garage, string meterKind) => meterKind switch
    {
        MeterKinds.Water => garage.InitialWaterMeterValue,
        MeterKinds.Electricity => garage.InitialElectricityMeterValue,
        _ => null
    };

    private static void SetGarageStartValueColumn(Garage garage, string meterKind, decimal? value)
    {
        if (meterKind == MeterKinds.Water)
        {
            garage.InitialWaterMeterValue = value;
        }
        else if (meterKind == MeterKinds.Electricity)
        {
            garage.InitialElectricityMeterValue = value;
        }
    }

    private static string FormatMeterValue(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU")) : "не задано";

    private static FinanceResult<GarageMeterStartValueApplyResult> StartValueFailure(string code, string message) =>
        FinanceResult<GarageMeterStartValueApplyResult>.Failure(code, message);
}
