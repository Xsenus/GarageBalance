using System.Text.RegularExpressions;

namespace GarageBalance.Api.Application.Audit;

public static partial class AuditTextMasker
{
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var masked = EmailRegex().Replace(value, "[email скрыт]");
        masked = BearerTokenRegex().Replace(masked, "Bearer [token скрыт]");
        masked = SecretAssignmentRegex().Replace(masked, match => $"{match.Groups["label"].Value}{match.Groups["separator"].Value}[секрет скрыт]");
        masked = LongNumberRegex().Replace(masked, "[номер скрыт]");
        return masked;
    }

    [GeneratedRegex("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("\\bBearer\\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?<label>\\b(?:password|passwd|pwd|пароль|token|токен|secret|секрет|api[_ -]?key|ключ)\\b)(?<separator>\\s*[:=]\\s*)(?<value>[^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?<!\\d)\\d{12,}(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberRegex();
}
