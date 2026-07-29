namespace GarageBalance.PerformanceSeed;

public sealed record PerformanceSeedOptions(int GarageCount, int MonthCount)
{
    public const int DefaultGarageCount = 500;
    public const int DefaultMonthCount = 60;

    public static PerformanceSeedOptions Parse(IReadOnlyList<string> arguments)
    {
        var garageCount = DefaultGarageCount;
        var monthCount = DefaultMonthCount;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is not "--garages" and not "--months")
            {
                throw new ArgumentException($"Unknown argument: {argument}");
            }

            if (++index >= arguments.Count || !int.TryParse(arguments[index], out var value))
            {
                throw new ArgumentException($"A whole-number value is required after {argument}.");
            }

            if (argument == "--garages")
            {
                garageCount = value;
            }
            else
            {
                monthCount = value;
            }
        }

        if (garageCount is < 10 or > 5000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                "Garage count must be between 10 and 5000.");
        }

        if (monthCount is < 12 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                "Month count must be between 12 and 120.");
        }

        return new PerformanceSeedOptions(garageCount, monthCount);
    }
}
