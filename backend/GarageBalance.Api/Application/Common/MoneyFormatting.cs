using System.Globalization;

namespace GarageBalance.Api.Application.Common;

public static class MoneyFormatting
{
    public static string Format(decimal value) =>
        value.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(',', ' ');
}
