namespace GarageBalance.Api.Application.Common;

public static class MonthPeriod
{
    public static DateOnly Normalize(DateOnly value)
    {
        return new DateOnly(value.Year, value.Month, 1);
    }

    public static DateOnly CurrentLocalMonth()
    {
        return Normalize(DateOnly.FromDateTime(DateTime.Today));
    }

    public static IEnumerable<DateOnly> Enumerate(DateOnly periodFrom, DateOnly periodTo)
    {
        for (var month = Normalize(periodFrom); month <= Normalize(periodTo); month = month.AddMonths(1))
        {
            yield return month;
        }
    }
}
