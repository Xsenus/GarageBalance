using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Domain.Finance;

namespace GarageBalance.Api.Application.Finance;

public sealed partial class FinanceService : IGarageAccountingStartService
{
    public async Task<FinanceResult<GarageAccountingStartChange>> BeginAsync(
        Garage garage, DateOnly month, Guid? expectedVersion, bool isNew,
        Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (!GarageAccountingPeriod.IsValidStartMonth(month))
            return AccountingStartFailure("garage_accounting_start_invalid", "Выберите месяц начала учёта не раньше января 1900 года.");

        var transaction = await meterReadingRepository.BeginConnectionWriteAsync(cancellationToken);
        var locks = new List<IAsyncDisposable>();
        var transferred = false;
        try
        {
            locks.Add(await accrualPaymentAllocationRepository.AcquireGarageIncomeWorksheetLockAsync(garage.Id, cancellationToken));
            var settings = await chargeServiceSettingRepository.GetActiveRegularAsync(GetCurrentAccountingMonth(), cancellationToken);
            var kinds = settings.Where(setting => MeterKinds.IsValid(setting.MeterKind)).Select(setting => setting.MeterKind!)
                .Concat(await meterReadingRepository.GetStoredMeterKindsAsync(garage.Id, cancellationToken))
                .Append(MeterKinds.Water).Append(MeterKinds.Electricity).Distinct(StringComparer.Ordinal).ToArray();
            if (kinds.Length > MaxAutomaticMeteredServices)
                return AccountingStartFailure("garage_accounting_start_meter_limit", "Для исправления начала учёта допускается не более 500 цепочек счётчиков одного гаража.");
            locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(
                kinds.Select(kind => GetMeterChainLockKey(garage.Id, kind)).ToArray(), cancellationToken));
            if (!isNew)
            {
                await garageRepository.ReloadForUpdateAsync(garage, cancellationToken);
                OptimisticConcurrencyGuard.EnsureCurrent(expectedVersion, garage);
                if (garage.IsArchived) return AccountingStartFailure("garage_not_found", "Гараж не найден.");
            }
            var oldMonth = GetGarageAccrualStartMonth(garage);
            var from = isNew || month < oldMonth ? month : oldMonth;
            var through = GetCurrentAccountingMonth();
            var historyThrough = !isNew && month > through ? month : through;
            if (from <= historyThrough && ((historyThrough.Year - from.Year) * 12 + historyThrough.Month - from.Month + 1) > MaxBalanceHistoryMonths)
                return AccountingStartFailure("garage_accounting_start_period_too_large", "Исправление начала учёта допускает пересчёт не более 600 месяцев.");

            if (!isNew)
            {
                var incomeTypeIds = await accrualRepository.GetActiveRegularIncomeTypeIdsAsync(
                    garage.Id, from, historyThrough, cancellationToken);
                var keys = incomeTypeIds.Select(incomeTypeId => new AccrualPaymentAllocationKey(garage.Id, incomeTypeId))
                    .Concat(settings.Where(setting => setting.IncomeTypeId.HasValue).Select(setting => new AccrualPaymentAllocationKey(garage.Id, setting.IncomeTypeId!.Value)))
                    .Distinct().ToArray();
                locks.Add(await accrualPaymentAllocationRepository.AcquireRebuildLockAsync(keys, cancellationToken));
                // Re-read after waiting for concurrent payments and generators.
                var existing = await accrualRepository.GetActiveRegularForGarageForUpdateAsync(
                    garage.Id, from, historyThrough, cancellationToken);
                var paid = await accrualPaymentAllocationRepository.GetActivelyAllocatedAccrualIdsAsync(
                    existing.Select(accrual => accrual.Id).ToArray(), cancellationToken);
                if (existing.Any(accrual => accrual.AccountingMonth < month && paid.Contains(accrual.Id)))
                    return AccountingStartFailure("garage_accounting_start_paid_history", "До выбранного начала учёта есть оплаченные или частично оплаченные начисления. Сначала оформите отдельную финансовую корректировку.");
                foreach (var kind in kinds)
                {
                    var first = await meterReadingRepository.GetFirstActiveAsync(garage.Id, kind, cancellationToken);
                    if (first is not null && first.AccountingMonth < month)
                        return AccountingStartFailure("garage_accounting_start_reading_history", "До выбранного начала учёта есть показания. Их историю нельзя скрыть переносом месяца.");
                }
            }

            garage.AccountingStartMonth = month;
            transferred = true;
            return FinanceResult<GarageAccountingStartChange>.Success(new(transaction, locks, async token =>
            {
                if (from <= through)
                {
                    var result = await CalculateGarageIncomeWorksheetAsync(garage.Id, new(from, through), actorUserId, token);
                    if (!result.Succeeded) return FinanceResult<bool>.Failure(result.ErrorCode!, result.ErrorMessage!);
                }
                return FinanceResult<bool>.Success(true);
            }));
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    foreach (var heldLock in locks.AsEnumerable().Reverse()) await heldLock.DisposeAsync();
                }
                finally { await transaction.DisposeAsync(); }
            }
        }
    }

    private static FinanceResult<GarageAccountingStartChange> AccountingStartFailure(string code, string message) =>
        FinanceResult<GarageAccountingStartChange>.Failure(code, message);
}
