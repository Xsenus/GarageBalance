using GarageBalance.Api.Domain.Dictionaries;

namespace GarageBalance.Api.Domain.Finance;

public readonly record struct AccrualDueDates(DateOnly DueDate, DateOnly OverdueFromDate)
{
    public static AccrualDueDates ForChargeService(DateOnly accountingMonth, ChargeServiceSetting? setting)
    {
        var month = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        if (setting?.PeriodicityMonths >= 12 && setting.PaymentDueDay.HasValue && setting.PaymentDueMonth.HasValue)
        {
            var dueDate = CreateClampedDate(month.Year, setting.PaymentDueMonth.Value, setting.PaymentDueDay.Value);
            if (dueDate < month)
            {
                dueDate = CreateClampedDate(month.Year + 1, setting.PaymentDueMonth.Value, setting.PaymentDueDay.Value);
            }

            return FromDueDate(dueDate, setting.OverdueGraceDays);
        }

        var dueMonth = month.AddMonths(1);
        var dueDay = setting?.PaymentDueDay ?? DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month);
        return FromDueDate(CreateClampedDate(dueMonth.Year, dueMonth.Month, dueDay), setting?.OverdueGraceDays ?? 30);
    }

    public static AccrualDueDates ForFeeCampaign(DateOnly accountingMonth, DateOnly? campaignEndsOn, int overdueGraceDays)
    {
        var dueDate = campaignEndsOn ?? new DateOnly(
            accountingMonth.Year,
            accountingMonth.Month,
            DateTime.DaysInMonth(accountingMonth.Year, accountingMonth.Month));
        return FromDueDate(dueDate, overdueGraceDays);
    }

    public static AccrualDueDates FromDueDate(DateOnly dueDate, int overdueGraceDays)
    {
        if (overdueGraceDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overdueGraceDays));
        }

        return new AccrualDueDates(dueDate, dueDate.AddDays(overdueGraceDays + 1));
    }

    private static DateOnly CreateClampedDate(int year, int month, int day)
    {
        var clampedDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, clampedDay);
    }
}
