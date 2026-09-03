using GarageBalance.Api.Domain.Dictionaries;
using GarageBalance.Api.Application.Common;

namespace GarageBalance.Api.Application.Finance;

public static class StaffSalaryTimeline
{
    public static decimal CalculateBaseAccrual(
        DateOnly monthFrom,
        DateOnly monthTo,
        decimal currentRate,
        DateOnly createdMonth,
        bool isArchived,
        DateOnly updatedMonth,
        IReadOnlyList<StaffSalaryRatePeriod> ratePeriods,
        IReadOnlyList<StaffEmploymentPeriod> employmentPeriods)
    {
        if (monthTo < monthFrom)
        {
            return 0m;
        }

        var normalizedFrom = MonthPeriod.Normalize(monthFrom);
        var normalizedTo = MonthPeriod.Normalize(monthTo);
        var orderedRates = ratePeriods.OrderBy(period => period.EffectiveFrom).ToArray();
        var total = 0m;
        for (var month = normalizedFrom; month <= normalizedTo; month = month.AddMonths(1))
        {
            if (!IsEmployed(month, createdMonth, isArchived, updatedMonth, employmentPeriods))
            {
                continue;
            }

            var rate = GetRate(month, currentRate, orderedRates);
            total += rate;
        }

        return MoneyMath.RoundMoney(total);
    }

    public static decimal GetRate(DateOnly month, decimal currentRate, IReadOnlyList<StaffSalaryRatePeriod> ratePeriods) =>
        ratePeriods.LastOrDefault(period => period.EffectiveFrom <= month)?.Rate ?? currentRate;

    public static bool IsEmployed(
        DateOnly month,
        DateOnly createdMonth,
        bool isArchived,
        DateOnly updatedMonth,
        IReadOnlyList<StaffEmploymentPeriod> employmentPeriods)
    {
        if (employmentPeriods.Count == 0)
        {
            return month >= createdMonth && (!isArchived || month <= updatedMonth);
        }

        return employmentPeriods.Any(period =>
            period.EffectiveFrom <= month &&
            (!period.EffectiveTo.HasValue || period.EffectiveTo.Value >= month));
    }
}
