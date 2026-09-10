using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Application.Finance;

public sealed class TariffAccrualRecalculationService(
    IAccrualRepository accrualRepository,
    IChargeServiceSettingRepository chargeServiceSettingRepository,
    IRegularAccrualRecalculationService financeService) : ITariffAccrualRecalculationService
{
    public async Task RecalculateExistingUnpaidAsync(
        Guid chargeServiceId,
        Guid incomeTypeId,
        DateOnly affectedFrom,
        Guid? actorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var monthFrom = new DateOnly(affectedFrom.Year, affectedFrom.Month, 1);
        var months = await accrualRepository.GetActiveRegularMonthsForRecalculationAsync(
            incomeTypeId, monthFrom, cancellationToken);
        if (months.Count == 0)
        {
            return;
        }

        var schedule = await chargeServiceSettingRepository.GetActiveTariffScheduleAsync(
            chargeServiceId, cancellationToken);
        if (!schedule.ServiceExists)
        {
            throw new InvalidOperationException("Услуга исчезла до автоматического перерасчёта начислений.");
        }

        var setting = schedule.Periods.Count == 0
            ? await chargeServiceSettingRepository.FindActiveAsync(chargeServiceId, cancellationToken)
            : null;

        foreach (var month in months)
        {
            var tariffId = schedule.Periods.Count == 0
                ? setting?.TariffId
                : ResolveTariffId(schedule.Periods, month);
            if (!tariffId.HasValue)
            {
                // An explicitly configured gap means there is intentionally no
                // tariff for this month, so existing history must not be guessed.
                continue;
            }

            var previewResult = await financeService.PreviewRegularAccrualRecalculationAsync(
                new PreviewRegularAccrualRecalculationRequest(incomeTypeId, tariffId.Value, month),
                cancellationToken);
            if (!previewResult.Succeeded || previewResult.Value is null)
            {
                throw new InvalidOperationException(previewResult.ErrorMessage ?? "Не удалось подготовить автоматический перерасчёт начислений.");
            }

            var applyResult = await financeService.ApplyRegularAccrualRecalculationAsync(
                new ApplyRegularAccrualRecalculationRequest(
                    incomeTypeId,
                    tariffId.Value,
                    month,
                    previewResult.Value.PreviewFingerprint,
                    reason),
                actorUserId,
                cancellationToken);
            if (!applyResult.Succeeded)
            {
                throw new InvalidOperationException(applyResult.ErrorMessage ?? "Не удалось применить автоматический перерасчёт начислений.");
            }
        }
    }

    private static Guid? ResolveTariffId(
        IReadOnlyList<ChargeServiceTariffVersion> periods,
        DateOnly accountingMonth)
    {
        var monthEnd = accountingMonth.AddMonths(1).AddDays(-1);
        return periods
            .Where(period =>
                !period.IsArchived &&
                !period.Tariff.IsArchived &&
                period.EffectiveFrom <= monthEnd &&
                (!period.EffectiveTo.HasValue || period.EffectiveTo.Value >= accountingMonth))
            .OrderByDescending(period => period.EffectiveFrom)
            .Select(period => (Guid?)period.TariffId)
            .FirstOrDefault();
    }
}
