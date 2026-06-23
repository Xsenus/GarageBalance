namespace GarageBalance.Api.Application.Common;

public static class MoneyMath
{
    public static decimal RoundMoney(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal RoundRate(decimal value)
    {
        return Math.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal RoundMeterValue(decimal value)
    {
        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }

    public static decimal? RoundMeterValue(decimal? value)
    {
        return value is null ? null : RoundMeterValue(value.Value);
    }
}
