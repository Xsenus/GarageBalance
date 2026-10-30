using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService
{
    public async Task<FinanceResult<MeterDeviceReplacementDetailsDto>> GetMeterDeviceReplacementAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await meterReadingRepository.FindDeviceAsync(deviceId, cancellationToken);
        if (device?.ReplacedDeviceId is not { } oldId)
            return FinanceResult<MeterDeviceReplacementDetailsDto>.Failure("meter_device_not_found", "Сохранённая замена не найдена.");
        var old = await meterReadingRepository.FindDeviceAsync(oldId, cancellationToken);
        if (old is null) return FinanceResult<MeterDeviceReplacementDetailsDto>.Failure("meter_device_not_found", "Снятый прибор не найден в истории.");
        var reading = device.ReplacementReadingId is { } readingId ? await meterReadingRepository.FindForUpdateAsync(readingId, cancellationToken) : null;
        return FinanceResult<MeterDeviceReplacementDetailsDto>.Success(new(ToDto(device), ToDto(old),
            reading is not null && reading.MeterDeviceId == device.Id ? ToDto(reading) : null));
    }

    public async Task<FinanceResult<MeterDeviceReplacementDetailsDto>> UpdateMeterDeviceReplacementAsync(
        Guid deviceId, UpdateMeterDeviceReplacementRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        FinanceResult<MeterDeviceReplacementDetailsDto> Failure(string code, string message) =>
            FinanceResult<MeterDeviceReplacementDetailsDto>.Failure(code, message);
        var reason = NormalizeOptional(request.Reason) ?? string.Empty;
        var serial = NormalizeOptional(request.NewSerialNumber);
        if (serial is null || serial.Length > 100 || reason.Length > 500 || ActionCommentRequirementContext.IsRequired && reason.Length == 0)
            return Failure("meter_device_values_invalid", "Укажите номер прибора и причину исправления замены.");
        if (request.RemovedDeviceFinalValue is null || request.NewInitialValue is null)
            return Failure("meter_device_final_required", "Укажите конечное показание старого и начальное показание нового прибора.");
        if (request.RemovedDeviceFinalValue is < 0m or > 999999999m || request.NewInitialValue is < 0m or > 999999999m)
            return Failure("meter_device_values_invalid", "Показания должны быть от 0 до 999 999 999.");
        var found = await meterReadingRepository.FindDeviceAsync(deviceId, cancellationToken);
        if (found is null) return Failure("meter_device_not_found", "Счётчик не найден.");
        await using var scope = await meterReadingRepository.BeginConnectionWriteAsync(cancellationToken);
        await using var chainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync([GetMeterChainLockKey(found.GarageId, found.MeterKind)], cancellationToken);
        var devices = (await meterReadingRepository.GetDevicesForUpdateAsync(found.GarageId, found.MeterKind, cancellationToken)).ToArray();
        var device = devices.SingleOrDefault(item => item.Id == deviceId);
        if (device is null || device.IsCanceled || device.ReplacedDeviceId is null || device.ReplacementAccountingMonth is null)
            return Failure("meter_device_not_replacement", "Эту запись нельзя изменить как действующую замену.");
        var old = devices.SingleOrDefault(item => item.Id == device.ReplacedDeviceId);
        var readings = (await meterReadingRepository.GetAllActiveForUpdateAsync(found.GarageId, found.MeterKind, cancellationToken)).ToArray();
        var target = readings.SingleOrDefault(item => item.Id == device.ReplacementReadingId && item.MeterDeviceId == device.Id);
        if (old is null || target is null)
            return Failure("meter_device_later_replacement", "Показание уже относится к другой замене. Сначала отмените последующую замену этого месяца.");
        if (device.Version != request.ExpectedDeviceVersion || old.Version != request.ExpectedRemovedDeviceVersion || target.Version != request.ExpectedReadingVersion)
            return Failure("meter_device_conflict", "Прибор или показание уже изменены. Откройте карточку заново.");
        if (request.AccountingMonth != target.AccountingMonth || MonthPeriod.Normalize(request.ReplacementDate) != target.AccountingMonth)
            return Failure("meter_device_replacement_date_month_mismatch", "Дата исправления должна оставаться в месяце сохранённой замены.");
        var connectionContext = await ValidateMeterConnectionAsync(target.Garage, target.MeterKind, target.AccountingMonth, request.ReplacementDate, true, cancellationToken);
        if (!connectionContext.Succeeded) return Failure(connectionContext.ErrorCode!, connectionContext.ErrorMessage!);
        if (request.ReplacementDate <= MeterBaselineResolver.GetEffectiveStartDate(old, target.Garage.AccountingStartMonth, devices) || device.RemovedOn is { } removedOn && request.ReplacementDate > removedOn ||
            readings.Any(item => item.Id != target.Id && item.MeterDeviceId == device.Id && item.ReadingDate < request.ReplacementDate) ||
            readings.Any(item => item.MeterDeviceId == old.Id && item.AccountingMonth == target.AccountingMonth && item.ReadingDate >= request.ReplacementDate))
            return Failure("meter_device_replacement_date_invalid", "Дата пересекает историю показаний или период другого прибора.");
        if (devices.Any(item => item.Id != device.Id && string.Equals(item.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)))
            return Failure("meter_device_serial_duplicate", "Такой номер прибора уже есть в истории гаража.");
        var final = MoneyMath.RoundMeterValue(request.RemovedDeviceFinalValue.Value);
        var initial = MoneyMath.RoundMeterValue(request.NewInitialValue.Value);
        var minimumFinal = readings.Where(item => item.MeterDeviceId == old.Id).Select(item => item.CurrentValue).DefaultIfEmpty(old.InitialValue).Max();
        if (final < minimumFinal || final < old.InitialValue)
            return Failure("meter_device_final_below_last_reading", "Конечное показание меньше сохранённых показаний старого прибора.");
        if (readings.Any(item => item.MeterDeviceId == device.Id && item.CurrentValue < initial) || device.FinalValue is { } deviceFinal && deviceFinal < initial)
            return Failure("meter_device_current_below_initial", "Начальное показание нового прибора выше его сохранённых показаний.");

        var prospective = devices.Select(item => new MeterDevice
        {
            Id = item.Id,
            GarageId = item.GarageId,
            MeterKind = item.MeterKind,
            SerialNumber = item.Id == device.Id ? serial : item.SerialNumber,
            InstalledOn = item.Id == device.Id ? request.ReplacementDate : item.InstalledOn,
            RemovedOn = item.Id == old.Id ? request.ReplacementDate.AddDays(-1) : item.RemovedOn,
            InitialValue = item.Id == device.Id ? initial : item.InitialValue,
            FinalValue = item.Id == old.Id ? final : item.FinalValue,
            ReplacedDeviceId = item.ReplacedDeviceId,
            ReplacementAccountingMonth = item.ReplacementAccountingMonth
        }).ToDictionary(item => item.Id);
        var chain = readings.Where(item => item.AccountingMonth >= target.AccountingMonth)
            .Select(item => CloneMeterReadingForChain(item, item.MeterDeviceId is { } id ? prospective.GetValueOrDefault(id) : null)).ToArray();
        chain.Single(item => item.Id == target.Id).ReadingDate = request.ReplacementDate;
        var previous = readings.Where(item => item.AccountingMonth < target.AccountingMonth).OrderByDescending(item => item.AccountingMonth).FirstOrDefault();
        var plan = await PlanMeterReadingChainAsync(target.Garage, target.MeterKind, previous, chain, cancellationToken, prospective.Values.ToArray());
        if (!plan.Succeeded) return Failure(plan.ErrorCode!, plan.ErrorMessage!);
        var errors = new List<string>();
        var recalculations = await PlanMeteredAccrualRecalculationsAsync(target.GarageId, target.MeterKind, target.AccountingMonth,
            plan.Value!, null, cancellationToken, validationErrors: errors);
        if (errors.Count > 0) return Failure("meter_connection_calculation_invalid", errors[0]);
        var keys = recalculations.Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId)).Distinct().ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(keys, cancellationToken);
        var before = new Dictionary<string, object?> { ["date"] = device.InstalledOn, ["serial"] = device.SerialNumber, ["initial"] = device.InitialValue, ["oldFinal"] = old.FinalValue, ["reason"] = device.ReplacementReason };
        device.InstalledOn = request.ReplacementDate;
        device.SerialNumber = serial;
        device.InitialValue = initial;
        device.ReplacementReason = reason;
        old.RemovedOn = request.ReplacementDate.AddDays(-1);
        old.FinalValue = final;
        device.Version = Guid.NewGuid(); old.Version = Guid.NewGuid();
        device.UpdatedAtUtc = old.UpdatedAtUtc = timeProvider.GetUtcNow();
        target.ReadingDate = request.ReplacementDate;
        target.Comment = $"Замена счётчика: {reason}";
        var originals = readings.ToDictionary(item => item.Id);
        ApplyMeterReadingChainChanges(plan.Value!.Select(change => change with
        {
            Reading = originals[change.Reading.Id],
            Changed = change.Changed || change.Reading.Id == target.Id
        }).ToArray(), actorUserId, target.Id);
        ApplyMeteredAccrualRecalculations(recalculations, actorUserId, "Пересчёт после исправления замены счётчика");
        AddAudit(actorUserId, "finance.meter_device_replacement_updated", "meter_device", device.Id, "Исправлена сохранённая замена счётчика.",
            relatedGarageId: target.GarageId.ToString(), relatedGarageNumber: target.Garage.Number, oldValues: before,
            newValues: new Dictionary<string, object?> { ["date"] = device.InstalledOn, ["serial"] = serial, ["initial"] = initial, ["oldFinal"] = final, ["reason"] = reason });
        await RebuildPaymentAllocationsAsync(keys, actorUserId, "Исправление замены счётчика", device.Id, cancellationToken);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); await scope.CommitAsync(cancellationToken); }
        catch (ApplicationConcurrencyException) { return Failure("meter_device_conflict", "История уже изменена другим пользователем."); }
        catch (ApplicationPersistenceConflictException) { return Failure("meter_device_conflict", "Не удалось сохранить из-за конкурентного изменения."); }
        return FinanceResult<MeterDeviceReplacementDetailsDto>.Success(new(ToDto(device), ToDto(old), ToDto(target)));
    }
}
