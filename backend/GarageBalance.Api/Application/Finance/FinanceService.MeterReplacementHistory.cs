using System.Text.Json;
using GarageBalance.Api.Application.Settings;
using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService
{
    public async Task<FinancePagedResult<MeterDeviceDto>> GetMeterDeviceHistoryAsync(
        Guid garageId, string? meterKind, int offset, int limit, CancellationToken cancellationToken)
    {
        var kind = NormalizeOptional(meterKind);
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        if (kind is not null && !MeterKinds.IsValid(kind))
        {
            return new FinancePagedResult<MeterDeviceDto>([], 0, offset, limit);
        }

        var page = await meterReadingRepository.GetDeviceHistoryPageAsync(garageId, kind, offset, limit, cancellationToken);
        var versions = await meterReadingRepository.GetActiveReadingVersionsAsync(
            page.Items.Where(device => device.ReplacementReadingId.HasValue).Select(device => device.ReplacementReadingId!.Value).Distinct().ToArray(), cancellationToken);
        var settings = await chargeServiceSettingRepository.GetListAsync(null, true, true, true, MaxAutomaticMeteredServices, businessDateProvider.Today, cancellationToken);
        return new FinancePagedResult<MeterDeviceDto>(page.Items.Select(device => ToDto(device) with
        {
            ReplacementReadingVersion = device.ReplacementReadingId.HasValue && versions.TryGetValue(device.ReplacementReadingId.Value, out var version) ? version : null,
            ServiceName = settings.Where(setting => setting.MeterKind == device.MeterKind).OrderBy(setting => setting.IsArchived).FirstOrDefault()?.Name
                ?? (device.MeterKind == MeterKinds.Water ? "Вода" : device.MeterKind == MeterKinds.Electricity ? "Электроэнергия" : "Счётчик услуги")
        }).ToArray(), page.TotalCount, offset, limit);
    }

    public async Task<FinanceResult<MeterDeviceReplacementCancellationDto>> CancelMeterDeviceReplacementAsync(
        Guid deviceId, CancelMeterDeviceReplacementRequest request, Guid? actorUserId, CancellationToken cancellationToken)
    {
        FinanceResult<MeterDeviceReplacementCancellationDto> Failure(string code, string message) =>
            FinanceResult<MeterDeviceReplacementCancellationDto>.Failure(code, message);
        var reason = NormalizeOptional(request.Reason) ?? string.Empty;
        if (ActionCommentRequirementContext.IsRequired && reason.Length == 0 || reason.Length > 500)
        {
            return Failure("meter_device_cancel_reason_required", "Укажите причину отмены замены (до 500 символов).");
        }

        var found = await meterReadingRepository.FindDeviceAsync(deviceId, cancellationToken);
        if (found is null) return Failure("meter_device_not_found", "Счётчик не найден.");
        if (found.IsCanceled) return Failure("meter_device_already_canceled", "Эта замена уже отменена.");
        if (!found.ReplacedDeviceId.HasValue || !found.ReplacementAccountingMonth.HasValue)
            return Failure("meter_device_not_replacement", "Этот прибор не относится к сохранённой замене.");
        if (MonthPeriod.Normalize(request.AccountingMonth) != found.ReplacementAccountingMonth)
            return Failure("meter_device_replacement_month_mismatch", "Месяц не совпадает с отменяемой заменой.");

        await using var chainLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
            [GetMeterChainLockKey(found.GarageId, found.MeterKind)], cancellationToken);
        var devices = (await meterReadingRepository.GetDevicesForUpdateAsync(found.GarageId, found.MeterKind, cancellationToken)).ToList();
        var device = devices.SingleOrDefault(item => item.Id == deviceId);
        if (device is null) return Failure("meter_device_already_canceled", "Эта замена уже отменена.");
        if (device.Version != request.ExpectedDeviceVersion)
            return Failure("meter_device_conflict", "История приборов изменена другим пользователем. Обновите её.");
        if (device.RemovedOn.HasValue || devices.Any(item => item.ReplacedDeviceId == device.Id))
            return Failure("meter_device_later_replacement", "Сначала отмените более позднюю замену счётчика.");
        var predecessor = devices.SingleOrDefault(item => item.Id == device.ReplacedDeviceId);
        if (predecessor is null) return Failure("meter_device_not_replacement", "Снимаемый прибор не найден в истории.");
        var readings = await meterReadingRepository.GetAllActiveForUpdateAsync(device.GarageId, device.MeterKind, cancellationToken);
        var target = readings.SingleOrDefault(item => item.Id == device.ReplacementReadingId);
        if (target is null) return Failure("meter_reading_not_found", "Переходное показание замены не найдено.");
        if (target.Version != request.ExpectedReadingVersion)
            return Failure("meter_reading_conflict", "Показание изменено другим пользователем. Обновите историю.");
        if (target.ReadingDate > device.InstalledOn)
            return Failure("meter_device_later_readings", "Показание месяца обновлено после замены. Сначала восстановите показание на дату замены, затем повторите отмену.");
        if (readings.Any(item => item.Id != target.Id && item.AccountingMonth > target.AccountingMonth))
            return Failure("meter_device_later_readings", "Сначала отмените последующие показания этого счётчика, затем повторите отмену замены.");

        MeterReadingBeforeReplacement? snapshot = null;
        if (device.HasReplacementSnapshot && device.PreviousReadingSnapshotJson is not null)
        {
            try
            {
                snapshot = JsonSerializer.Deserialize<MeterReadingBeforeReplacement>(device.PreviousReadingSnapshotJson, PersistedJsonOptions);
            }
            catch (JsonException)
            {
                return Failure("meter_device_snapshot_invalid", "Сохранённое состояние до замены повреждено. Отмена не выполнена.");
            }
            if (snapshot is null) return Failure("meter_device_snapshot_invalid", "Сохранённое состояние до замены отсутствует. Отмена не выполнена.");
        }
        else if (!device.HasReplacementSnapshot)
        {
            if (request.RestoredCurrentValue is null or < 0m or > 999999999m)
                return Failure("meter_device_restored_value_required", "Для старой замены укажите фактическое показание восстанавливаемого прибора.");
            snapshot = new MeterReadingBeforeReplacement(device.InstalledOn, MoneyMath.RoundMeterValue(request.RestoredCurrentValue.Value),
                0m, 0m, predecessor.ReplacedDeviceId.HasValue && MonthPeriod.Normalize(predecessor.InstalledOn) == target.AccountingMonth,
                false, $"Показание восстановлено при отмене старой замены: {reason}");
        }

        var previous = readings.Where(item => item.AccountingMonth < target.AccountingMonth)
            .OrderByDescending(item => item.AccountingMonth).FirstOrDefault();
        var originalCurrentValue = target.CurrentValue;
        var restored = CloneMeterReadingForChain(target, predecessor);
        if (snapshot is not null) snapshot.Apply(restored);
        var plan = await PlanMeterReadingChainAsync(target.Garage, target.MeterKind, previous,
            snapshot is null ? [] : [restored], cancellationToken, devices.Where(item => item.Id != device.Id).ToArray());
        if (!plan.Succeeded) return Failure(plan.ErrorCode!, plan.ErrorMessage!);
        var recalculations = await PlanMeteredAccrualRecalculationsAsync(target.GarageId, target.MeterKind,
            target.AccountingMonth, plan.Value!, snapshot is null ? target.AccountingMonth : null, cancellationToken);
        var keys = recalculations.Select(item => new AccrualPaymentAllocationKey(item.Accrual.GarageId, item.Accrual.IncomeTypeId)).Distinct().ToArray();
        await using var allocationLock = await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(keys, cancellationToken);

        device.IsCanceled = true;
        device.CancellationReason = reason;
        device.CanceledAtUtc = timeProvider.GetUtcNow();
        device.Version = Guid.NewGuid();
        device.UpdatedAtUtc = timeProvider.GetUtcNow();
        predecessor.RemovedOn = null;
        predecessor.FinalValue = null;
        predecessor.Version = Guid.NewGuid();
        predecessor.UpdatedAtUtc = timeProvider.GetUtcNow();
        if (snapshot is null)
        {
            target.IsCanceled = true;
            target.Comment = AppendCancelReason(target.Comment, reason);
        }
        else
        {
            snapshot.Apply(target);
            target.MeterDeviceId = predecessor.Id;
            target.MeterDevice = predecessor;
            var change = plan.Value![0];
            target.PreviousValue = change.PreviousValue;
            target.PreviousDeviceConsumption = change.PreviousDeviceConsumption;
            target.Consumption = change.Consumption;
            target.HasGapWarning = change.HasGapWarning;
        }
        target.Version = Guid.NewGuid();
        target.UpdatedAtUtc = timeProvider.GetUtcNow();
        ApplyMeteredAccrualRecalculations(recalculations, actorUserId, "Пересчет после отмены замены счётчика");
        AddAudit(actorUserId, "finance.meter_device_replacement_canceled", "meter_device", device.Id,
            $"В гараже {target.Garage.Number} отменена замена на № {device.SerialNumber}; восстановлен № {predecessor.SerialNumber}. Причина: {reason}.",
            metadata: new Dictionary<string, object?>
            {
                ["garageId"] = target.GarageId,
                ["meterKind"] = target.MeterKind,
                ["restoredDeviceId"] = predecessor.Id,
                ["replacementDate"] = device.InstalledOn,
                ["reason"] = reason
            },
            oldValues: new Dictionary<string, object?> { ["isCanceled"] = false, ["currentValue"] = originalCurrentValue },
            newValues: new Dictionary<string, object?> { ["isCanceled"] = true, ["restoredCurrentValue"] = snapshot?.CurrentValue });
        await RebuildPaymentAllocationsAsync(keys, actorUserId, "Перераспределение после отмены замены счётчика", target.Id, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ApplicationConcurrencyException)
        {
            return Failure("meter_device_conflict", "Прибор или показание уже изменены. Обновите историю и повторите отмену.");
        }
        catch (ApplicationPersistenceConflictException)
        {
            return Failure("meter_device_conflict", "Не удалось отменить замену из-за конкурентного изменения. Обновите историю.");
        }
        return FinanceResult<MeterDeviceReplacementCancellationDto>.Success(new(ToDto(predecessor), ToDto(target)));
    }

    private sealed record MeterReadingBeforeReplacement(
        DateOnly ReadingDate, decimal CurrentValue, decimal PreviousValue, decimal PreviousDeviceConsumption,
        bool IsMeterReplacement, bool HasGapWarning, string? Comment)
    {
        public static MeterReadingBeforeReplacement From(MeterReading reading) =>
            new(reading.ReadingDate, reading.CurrentValue, reading.PreviousValue, reading.PreviousDeviceConsumption,
                reading.IsMeterReplacement, reading.HasGapWarning, reading.Comment);

        public void Apply(MeterReading reading)
        {
            reading.ReadingDate = ReadingDate;
            reading.CurrentValue = CurrentValue;
            reading.PreviousValue = PreviousValue;
            reading.PreviousDeviceConsumption = PreviousDeviceConsumption;
            reading.IsMeterReplacement = IsMeterReplacement;
            reading.HasGapWarning = HasGapWarning;
            reading.Comment = Comment;
        }
    }
}
