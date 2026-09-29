using GarageBalance.Api.Application.Common;
using GarageBalance.Api.Application.Dictionaries;
using GarageBalance.Api.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace GarageBalance.Api.Infrastructure.Data;

public sealed class EfGarageTariffAssignmentQuery(GarageBalanceDbContext context) : IGarageTariffAssignmentQuery
{
    public async Task<IReadOnlyList<GarageTariffAssignment>> GetForGaragePeriodAsync(
        Guid garageId, DateOnly monthFrom, DateOnly monthTo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var from = MonthPeriod.Normalize(monthFrom);
        var throughMonth = MonthPeriod.Normalize(monthTo);
        if (from > throughMonth || (throughMonth.Year - from.Year) * 12 + throughMonth.Month - from.Month >= 600)
            throw new ArgumentException("Период индивидуальных тарифов должен содержать от 1 до 600 месяцев.", nameof(monthTo));
        var to = throughMonth.AddMonths(1).AddDays(-1);
        // A worksheet reads the complete selected timeline once, not for each month.
        // Bound unusually large/corrupt histories without calculating a partial result.
        var periods = await context.GarageTariffAssignments.AsNoTracking().Include(item => item.Tariff)
            .Where(item => item.GarageId == garageId && !item.IsArchived
                && item.EffectiveFrom <= to && (item.EffectiveTo == null || item.EffectiveTo >= from))
            .OrderBy(item => item.ChargeServiceSettingId).ThenBy(item => item.EffectiveFrom).ThenBy(item => item.Id)
            .Take(10001).ToListAsync(cancellationToken);
        if (periods.Count > 10000)
            throw new InvalidOperationException("Период содержит более 10000 назначений индивидуальных тарифов. Сократите период ведомости.");
        if (periods.GroupBy(item => item.ChargeServiceSettingId).Any(group => group.Zip(group.Skip(1),
            (left, right) => !left.EffectiveTo.HasValue || left.EffectiveTo.Value >= right.EffectiveFrom).Any(overlap => overlap)))
            throw new InvalidOperationException("Индивидуальные тарифные периоды гаража пересекаются. Исправьте назначения до расчёта начислений.");
        return periods;
    }

    public Task<IReadOnlyList<GarageTariffAssignment>> GetApplicableAsync(
        Guid garageId, Guid chargeServiceSettingId, DateOnly accountingMonth, CancellationToken cancellationToken)
        => GetApplicableForGaragesAsync([garageId], chargeServiceSettingId, accountingMonth, cancellationToken);

    public async Task<IReadOnlyList<GarageTariffAssignment>> GetApplicableForGaragesAsync(
        IReadOnlyCollection<Guid> garageIds, Guid chargeServiceSettingId, DateOnly accountingMonth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = garageIds.Distinct().ToArray();
        if (ids.Length > 500)
            throw new ArgumentException("Одна порция расчёта может содержать не более 500 гаражей.", nameof(garageIds));
        if (ids.Length == 0) return [];
        var from = MonthPeriod.Normalize(accountingMonth);
        var to = from.AddMonths(1).AddDays(-1);
        // Date-only, non-overlapping periods can cover a month with at most one
        // distinct period per day. Fetch one extra to detect corrupt history.
        var periods = await context.GarageTariffAssignments.AsNoTracking().Include(item => item.Tariff)
            .Where(item => ids.Contains(item.GarageId) && item.ChargeServiceSettingId == chargeServiceSettingId
                && !item.IsArchived && item.EffectiveFrom <= to && (item.EffectiveTo == null || item.EffectiveTo >= from))
            .OrderBy(item => item.GarageId).ThenBy(item => item.EffectiveFrom).ThenBy(item => item.Id)
            .Take(ids.Length * to.Day + 1).ToListAsync(cancellationToken);
        // One bounded server query per batch, not one round-trip for every garage.
        // If the cap is exceeded the result is rejected, never silently truncated.
        if (periods.Count > ids.Length * to.Day || periods.GroupBy(item => item.GarageId).Any(group =>
            group.Count() > to.Day || group.Zip(group.Skip(1), (left, right) =>
                !left.EffectiveTo.HasValue || left.EffectiveTo.Value >= right.EffectiveFrom).Any(overlap => overlap)))
        {
            throw new InvalidOperationException("Индивидуальные тарифные периоды гаража пересекаются. Исправьте назначения до расчёта начислений.");
        }
        return periods;
    }
}
