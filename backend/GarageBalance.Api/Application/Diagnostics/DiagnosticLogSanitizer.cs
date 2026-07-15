using System.Text.RegularExpressions;

namespace GarageBalance.Api.Application.Diagnostics;

public static partial class DiagnosticLogSanitizer
{
    private const int MaximumLength = 20_000;

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value;
        sanitized = BearerToken().Replace(sanitized, "Bearer [redacted]");
        sanitized = SecretAssignment().Replace(sanitized, "$1=[redacted]");
        sanitized = EmailAddress().Replace(sanitized, "[email]");
        sanitized = PhoneNumber().Replace(sanitized, "[phone]");
        return sanitized.Length <= MaximumLength ? sanitized : sanitized[..MaximumLength] + "…[truncated]";
    }

    public static string SanitizeException(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            parts.Add(current.GetType().FullName ?? current.GetType().Name);
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                parts.Add(current.StackTrace);
            }
        }

        return Sanitize(string.Join(Environment.NewLine, parts));
    }

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9._~+\-/]+=*")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)\b(password|pwd|token|secret|api[_-]?key|authorization)\s*[=:]\s*[^;\s,]+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<!\d)(?:\+?7|8)[\s()\-]*\d{3}[\s()\-]*\d{3}[\s\-]*\d{2}[\s\-]*\d{2}(?!\d)")]
    private static partial Regex PhoneNumber();
}
