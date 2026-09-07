using System.Globalization;
using System.Text.RegularExpressions;

namespace GarageBalance.Api.Domain.Finance;

public static partial class MeteredAccrualComments
{
    public static string? Refresh(string? comment, decimal? consumption)
    {
        if (consumption.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(consumption.Value);
        }
        if (string.IsNullOrEmpty(comment))
        {
            return comment;
        }
        var separator = comment.IndexOf(';');
        var heading = separator < 0 ? comment : comment[..separator];
        var match = GeneratedHeading().Match(heading);
        if (!match.Success)
        {
            return comment;
        }
        var explanation = consumption.HasValue
            ? $"расход {consumption.Value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"))}"
            : "показание отменено";
        return $"Начисление по показанию {match.Groups[1].Value}: {explanation}" + (separator < 0 ? string.Empty : comment[separator..]);
    }

    [GeneratedRegex(@"\AНачисление по показанию (water|electricity): (?:расход\s*[0-9]+(?:[.,][0-9]+)?|показание отменено)\z", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedHeading();
}
