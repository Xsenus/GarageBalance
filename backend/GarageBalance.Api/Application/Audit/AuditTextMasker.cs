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
        masked = PersonalDataAssignmentRegex().Replace(masked, match => $"{match.Groups["label"].Value}{match.Groups["separator"].Value}[персональные данные скрыты]");
        masked = PhoneRegex().Replace(masked, "[телефон скрыт]");
        masked = PassportRegex().Replace(masked, "[документ скрыт]");
        masked = LongNumberRegex().Replace(masked, "[номер скрыт]");
        return masked;
    }

    [GeneratedRegex("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex("\\bBearer\\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("(?<label>\\b(?:password|passwd|pwd|пароль|token|токен|secret|секрет|api[_ -]?key|ключ)\\b)(?<separator>\\s*[:=]\\s*)(?<value>[^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?<label>\\b(?:phone|телефон|address|адрес|passport|паспорт)\\b)(?<separator>\\s*[:=]\\s*)(?<value>[^,;\\r\\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonalDataAssignmentRegex();

    [GeneratedRegex("(?<!\\d)(?:\\+7|8)[\\s(.-]*\\d{3}[\\s).-]*\\d{3}[\\s.-]*\\d{2}[\\s.-]*\\d{2}(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex("(?<!\\d)\\d{4}[\\s-]?\\d{6}(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PassportRegex();

    [GeneratedRegex("(?<!\\d)\\d{12,}(?!\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumberRegex();
}
