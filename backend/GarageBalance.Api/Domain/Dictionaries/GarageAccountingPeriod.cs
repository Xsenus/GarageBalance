namespace GarageBalance.Api.Domain.Dictionaries;

public static class GarageAccountingPeriod
{
    public static bool IsValidStartMonth(DateOnly month) => month.Year >= 1900 && month.Day == 1;

    public static DateOnly ResolveStartMonth(DateOnly? explicitMonth, DateOnly registrationDate)
    {
        if (explicitMonth is { } month)
        {
            if (!IsValidStartMonth(month))
            {
                throw new ArgumentOutOfRangeException(nameof(explicitMonth), "Accounting start must be the first day of a month from 1900.");
            }

            return month;
        }

        return new DateOnly(registrationDate.Year, registrationDate.Month, 1);
    }
}
