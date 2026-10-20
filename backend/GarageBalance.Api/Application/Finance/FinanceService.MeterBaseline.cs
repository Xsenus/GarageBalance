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
        var connections = (await meterReadingRepository.GetConnectionsAsync(garage.Id, cancellationToken))
            .ToDictionary(connection => connection.MeterKind, StringComparer.Ordinal);
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
            connections.TryGetValue(meterKind, out var connection);
            var value = connection is null
                ? baseline?.InitialValue ?? GetGarageStartValueColumn(garage, meterKind)
                : connection.BaselineMeterDevice?.InitialValue;
            var hasLegacyHistory = baseline is not null || firstReading is not null || value.HasValue;
            values.Add(new GarageMeterStartValueDto(
                meterKind,
                setting.Name,
                setting.UnitName,
                value,
                firstReading is not null,
                connection?.ConnectedOn,
                connection is not null && !value.HasValue,
                connection is null && !hasLegacyHistory,
                connection?.Version,
                connection is not null && await RequiresConnectionTariffAsync(garage.Id, setting.Id, connection, cancellationToken),
                // Without a connection the baseline device (for example a meter accounting
                // restart boundary) can start later than "Учитывать с" and then defines the base month.
                connection is null ? baseline?.InstalledOn : null));
        }

        return values;
    }

    private const string GarageStartMeterReason = "Стартовое показание на начало учёта гаража";

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
        CancellationToken cancellationToken,
        bool alignConnectionsToGarageStart = false)
    {
        var locks = new List<IAsyncDisposable>();
        var transferred = false;
        try
        {
            var kinds = requested.Select(item => item?.MeterKind?.Trim()).ToList();
            if (kinds.Any(kind => !MeterKinds.IsValid(kind)))
            {
                return StartValueFailure("meter_kind_invalid", "Выберите действующую услугу по счётчику.");
            }

            if (requested.Any(item => item.Value is < 0m or > 999999999m))
            {
                return StartValueFailure("meter_start_value_invalid", "Стартовое показание должно быть числом от 0 до 999 999 999.");
            }

            if (requested.Any(item => item.Reason?.Trim().Length > 1000))
            {
                return StartValueFailure("meter_connection_reason_invalid", "Причина изменения не должна превышать 1000 символов.");
            }

            if (kinds.Count != kinds.Distinct(StringComparer.Ordinal).Count())
            {
                return StartValueFailure("meter_start_kind_duplicate", "Стартовое значение одного счётчика указано дважды.");
            }

            // Acquire the whole set in the repository's stable order, independently of field order.
            locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                kinds.Select(kind => GetMeterChainLockKey(garage.Id, kind!)).ToArray(),
                cancellationToken));
            IReadOnlyList<ChargeServiceSetting>? settings = null;
            var changes = new List<GarageMeterStartValueChange>();
            foreach (var item in requested)
            {
                var meterKind = item.MeterKind.Trim();
                settings ??= await GetStartValueSettingsAsync(garage, cancellationToken);
                var setting = settings.FirstOrDefault(candidate => candidate.MeterKind == meterKind);
                var isLegacyKind = meterKind is MeterKinds.Water or MeterKinds.Electricity;
                if (setting is null && (!isLegacyKind || item.ConnectedOn.HasValue))
                {
                    return StartValueFailure(
                        "meter_start_service_not_applicable",
                        "Услуга со счётчиком не применяется к этому гаражу. Обновите карточку и повторите действие.");
                }

                var label = setting?.Name ?? (meterKind == MeterKinds.Water ? "Вода" : "Электроэнергия");
                var step = await ApplyStartValueAsync(garage, meterKind, label, item, actorUserId, locks, cancellationToken,
                    alignConnectionsToGarageStart);
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
        UpsertGarageMeterStartValueRequest request,
        Guid? actorUserId,
        List<IAsyncDisposable> locks,
        CancellationToken cancellationToken,
        bool alignConnectionToGarageStart)
    {
        var devices = (await meterReadingRepository.GetDevicesForUpdateAsync(garage.Id, meterKind, cancellationToken)).ToList();
        var readings = (await meterReadingRepository.GetAllActiveForUpdateAsync(garage.Id, meterKind, cancellationToken)).ToList();
        var column = GetGarageStartValueColumn(garage, meterKind);
        var isLegacyKind = meterKind is MeterKinds.Water or MeterKinds.Electricity;
        var baseline = MeterBaselineResolver.FindBaselineDevice(devices, readings.FirstOrDefault());
        var connection = await meterReadingRepository.GetConnectionForUpdateAsync(garage.Id, meterKind, cancellationToken);
        if (!request.ConnectedOn.HasValue &&
            (connection is not null ? alignConnectionToGarageStart : baseline is null && readings.Count == 0 && column is null && request.Value.HasValue))
        {
            // The start value is the reading at the end of the month before the garage's
            // "Учитывать с" month. An explicit connection date stays available for services
            // connected later, but the card and the readings form follow the garage start.
            request = request with
            {
                ConnectedOn = GetGarageAccrualStartMonth(garage),
                ConnectionVersion = request.ConnectionVersion ?? connection?.Version,
                Reason = string.IsNullOrWhiteSpace(request.Reason) ? GarageStartMeterReason : request.Reason
            };
        }

        if (connection is not null || request.ConnectedOn.HasValue)
        {
            return await ApplyConnectionStartValueAsync(garage, meterKind, label, request, connection,
                baseline, devices, readings, actorUserId, locks, cancellationToken);
        }

        var requestedValue = request.Value;
        var oldValue = baseline?.InitialValue ?? column;

        if (!oldValue.HasValue && baseline is null && readings.Count == 0 && requestedValue.HasValue)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_date_required", $"Для услуги «{label}» укажите дату подключения и стартовое показание.");
        }

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

    private async Task<FinanceResult<GarageMeterStartValueChange?>> ApplyConnectionStartValueAsync(
        Garage garage,
        string meterKind,
        string label,
        UpsertGarageMeterStartValueRequest request,
        GarageMeterConnection? connection,
        MeterDevice? legacyBaseline,
        List<MeterDevice> devices,
        List<MeterReading> readings,
        Guid? actorUserId,
        List<IAsyncDisposable> locks,
        CancellationToken cancellationToken)
    {
        if (request.ConnectedOn is not { } connectedOn)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_date_required", $"Укажите дату подключения услуги «{label}».");
        }

        if (connectedOn.Year < 1900)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_date_invalid", "Дата подключения должна быть не раньше 01.01.1900.");
        }

        if (connection is not null)
        {
            if (!request.ConnectionVersion.HasValue)
            {
                return FinanceResult<GarageMeterStartValueChange?>.Failure(
                    "meter_connection_version_required", "Обновите карточку гаража перед изменением подключения услуги.");
            }

            OptimisticConcurrencyGuard.EnsureCurrent(request.ConnectionVersion, connection);
        }
        else if (request.ConnectionVersion.HasValue)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_changed", "Подключение услуги изменилось. Обновите карточку гаража.");
        }

        var baseline = connection is null ? legacyBaseline : connection.BaselineMeterDevice;
        var oldValue = connection is null ? baseline?.InitialValue ?? GetGarageStartValueColumn(garage, meterKind) : baseline?.InitialValue;
        var oldConnectedOn = connection?.ConnectedOn;
        var value = MoneyMath.RoundMeterValue(request.Value);
        if (connection is not null && oldConnectedOn == connectedOn && oldValue == value)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Success(null);
        }

        if ((connection is not null || readings.Count > 0 || legacyBaseline is not null || oldValue.HasValue) &&
            string.IsNullOrWhiteSpace(request.Reason))
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_reason_required", "Укажите причину изменения даты подключения или стартового показания.");
        }

        if (readings.Any(reading => reading.ReadingDate < connectedOn || reading.AccountingMonth < MonthPeriod.Normalize(connectedOn)))
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_date_after_history", "Дата подключения не может исключать уже внесённые показания. Сначала исправьте соответствующие показания.");
        }

        if (!value.HasValue && (readings.Count > 0 || devices.Any(device => device.RemovedOn.HasValue)))
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_start_value_required", "Нельзя удалить стартовое показание услуги с показаниями или историей замен.");
        }

        // A date belongs to the service accounting period, not to the physical installation.
        // In particular, adding a connection must not rewrite the garage's legacy opening columns.
        baseline ??= legacyBaseline;
        if (baseline?.FinalValue is { } finalValue && value > finalValue)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_start_above_final",
                $"Стартовое показание «{label}» не может быть больше конечного показания снятого счётчика ({FormatMeterValue(finalValue)}).");
        }

        if (baseline?.RemovedOn is { } removedOn && connectedOn > removedOn)
        {
            return FinanceResult<GarageMeterStartValueChange?>.Failure(
                "meter_connection_date_after_history", "Дата подключения не может быть позже снятия первого прибора.");
        }

        if (connection is null)
        {
            connection = new GarageMeterConnection
            {
                GarageId = garage.Id,
                Garage = garage,
                MeterKind = meterKind,
                ConnectedOn = connectedOn,
                CreatedAtUtc = timeProvider.GetUtcNow(),
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };
            meterReadingRepository.Add(connection);
        }

        connection.ConnectedOn = connectedOn;
        connection.Version = Guid.NewGuid();
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();
        if (value.HasValue)
        {
            if (baseline is null)
            {
                baseline = new MeterDevice
                {
                    GarageId = garage.Id,
                    Garage = garage,
                    MeterKind = meterKind,
                    SerialNumber = MeterBaselineResolver.UnnumberedDeviceSerial,
                    InstalledOn = connectedOn,
                    InitialValue = value.Value
                };
                meterReadingRepository.Add(baseline);
                devices.Insert(0, baseline);
            }
            else
            {
                baseline.InitialValue = value.Value;
                if (baseline.SerialNumber == MeterBaselineResolver.UnnumberedDeviceSerial &&
                    (baseline.InstalledOn == oldConnectedOn ||
                        connection.BaselineMeterDeviceId is null && readings.Count == 0 && baseline.RemovedOn is null))
                {
                    // This unnumbered device was created as the logical opening boundary.
                    // A dated correction must keep that boundary usable for the earlier days too.
                    baseline.InstalledOn = connectedOn;
                }
                baseline.Version = Guid.NewGuid();
                baseline.UpdatedAtUtc = timeProvider.GetUtcNow();
            }

            connection.BaselineMeterDevice = baseline;
            connection.BaselineMeterDeviceId = baseline.Id;
            foreach (var reading in readings.Where(reading => reading.MeterDeviceId is null))
            {
                reading.MeterDevice = baseline;
                reading.MeterDeviceId = baseline.Id;
            }

            if (readings.Count > 0)
            {
                var rebuild = await RebuildChainAfterStartValueChangeAsync(garage, meterKind, label, value.Value,
                    baseline, devices, readings, actorUserId, locks, cancellationToken, connection,
                    oldConnectedOn != connectedOn);
                if (!rebuild.Succeeded)
                {
                    return FinanceResult<GarageMeterStartValueChange?>.Failure(rebuild.ErrorCode!, rebuild.ErrorMessage!);
                }
            }
        }
        else
        {
            connection.BaselineMeterDevice = null;
            connection.BaselineMeterDeviceId = null;
        }

        AddAudit(actorUserId, "finance.meter_connection_changed", "garage", garage.Id,
            $"Изменено подключение услуги «{label}» в гараже {garage.Number}: {connectedOn:dd.MM.yyyy}, стартовое показание {FormatMeterValue(value)}.",
            reason: request.Reason?.Trim(), relatedGarageId: garage.Id.ToString(), relatedGarageNumber: garage.Number,
            metadata: new Dictionary<string, object?> { ["meterKind"] = meterKind },
            oldValues: new Dictionary<string, object?> { ["connectedOn"] = oldConnectedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["startValue"] = oldValue?.ToString("0.###", CultureInfo.InvariantCulture) },
            newValues: new Dictionary<string, object?> { ["connectedOn"] = connectedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["startValue"] = value?.ToString("0.###", CultureInfo.InvariantCulture) });
        return FinanceResult<GarageMeterStartValueChange?>.Success(
            new GarageMeterStartValueChange(meterKind, label, oldValue, value, oldConnectedOn, connectedOn));
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
        CancellationToken cancellationToken,
        GarageMeterConnection? connectionOverride = null,
        bool connectionDateChanged = false)
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

        var chainPlan = await PlanMeterReadingChainAsync(garage, meterKind, null, readings, cancellationToken, devices);
        if (!chainPlan.Succeeded)
        {
            return FinanceResult<bool>.Failure(
                "meter_start_value_breaks_readings",
                $"Стартовое показание {FormatMeterValue(value)} («{label}») не подходит к уже внесённым показаниям. {chainPlan.ErrorMessage}");
        }

        var calculationErrors = new List<string>();
        var accrualRecalculations = await PlanMeteredAccrualRecalculationsAsync(
            garage.Id,
            meterKind,
            readings[0].AccountingMonth,
            chainPlan.Value!.Where(change => change.Changed || connectionDateChanged).ToArray(),
            missingReadingMonth: null,
            cancellationToken,
            connectionOverride,
            calculationErrors);
        if (calculationErrors.Count > 0)
        {
            return FinanceResult<bool>.Failure("meter_connection_calculation_invalid", calculationErrors[0]);
        }
        var allocationKeys = accrualRecalculations
            .Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId))
            .Distinct()
            .ToArray();
        locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(allocationKeys, cancellationToken));

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
            oldValues: new Dictionary<string, object?> { ["startValue"] = oldValue?.ToString("0.###", CultureInfo.InvariantCulture) },
            newValues: new Dictionary<string, object?> { ["startValue"] = newValue?.ToString("0.###", CultureInfo.InvariantCulture) });
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
