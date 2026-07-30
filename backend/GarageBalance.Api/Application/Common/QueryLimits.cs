namespace GarageBalance.Api.Application.Common;

public static class QueryLimits
{
    public const int DefaultListSize = 100;
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 500;
    public const int MaximumReportExportRows = 5000;
    public const int MaximumReportPeriodMonths = 120;

    public static int NormalizeListSize(int? requestedSize, int defaultSize = DefaultListSize, int maximumSize = MaximumPageSize)
    {
        ValidateBounds(defaultSize, maximumSize);
        return requestedSize is null or <= 0
            ? defaultSize
            : Math.Min(requestedSize.Value, maximumSize);
    }

    public static int NormalizePageSize(int requestedSize, int defaultSize = DefaultPageSize, int maximumSize = MaximumPageSize)
    {
        ValidateBounds(defaultSize, maximumSize);
        return requestedSize <= 0
            ? defaultSize
            : Math.Min(requestedSize, maximumSize);
    }

    public static bool ExceedsMaximumReportPeriod(DateOnly periodFrom, DateOnly periodTo)
    {
        if (periodTo < periodFrom)
        {
            return false;
        }

        var inclusiveMonthCount =
            ((long)periodTo.Year - periodFrom.Year) * 12L +
            periodTo.Month -
            periodFrom.Month +
            1L;
        return inclusiveMonthCount > MaximumReportPeriodMonths;
    }

    private static void ValidateBounds(int defaultSize, int maximumSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSize);
        if (defaultSize > maximumSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultSize),
                defaultSize,
                "Размер страницы по умолчанию не может превышать максимальный размер.");
        }
    }
}
