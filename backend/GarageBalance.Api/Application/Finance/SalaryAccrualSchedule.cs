namespace GarageBalance.Api.Application.Finance;

public static class SalaryAccrualSchedule
{
    public static bool IsAccrued(DateOnly accountingMonth, DateOnly businessDate, int accrualDay)
    {
        var normalizedMonth = new DateOnly(accountingMonth.Year, accountingMonth.Month, 1);
        var businessMonth = new DateOnly(businessDate.Year, businessDate.Month, 1);
        if (normalizedMonth < businessMonth)
        {
            return true;
        }

        return normalizedMonth == businessMonth && businessDate.Day >= accrualDay;
    }
}
