using System.Globalization;

namespace GarageBalance.Api.Application.Audit;

public sealed record AuditChangeDiff(string FieldName, string? OldValue, string? NewValue);

public static class AuditChangeDiffBuilder
{
    private static readonly string[] SensitiveFieldNameParts =
    [
        "password",
        "passwd",
        "pwd",
        "пароль",
        "token",
        "токен",
        "secret",
        "секрет",
        "apikey",
        "api_key",
        "api-key",
        "ключ",
        "email",
        "почта",
        "phone",
        "телефон",
        "address",
        "адрес",
        "bank",
        "банк",
        "account",
        "расчетныйсчет",
        "расчетныйсчёт",
        "банковскийсчет",
        "банковскийсчёт",
        "лицевойсчет",
        "лицевойсчёт",
        "passport",
        "паспорт"
    ];

    public static IReadOnlyList<AuditChangeDiff> Build(
        IReadOnlyDictionary<string, object?> oldValues,
        IReadOnlyDictionary<string, object?> newValues,
        IReadOnlyDictionary<string, string>? fieldLabels = null)
    {
        ArgumentNullException.ThrowIfNull(oldValues);
        ArgumentNullException.ThrowIfNull(newValues);

        var fieldNames = oldValues.Keys
            .Concat(newValues.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var changes = new List<AuditChangeDiff>();
        foreach (var fieldName in fieldNames)
        {
            oldValues.TryGetValue(fieldName, out var oldValue);
            newValues.TryGetValue(fieldName, out var newValue);

            var normalizedOldValue = NormalizeValue(oldValue);
            var normalizedNewValue = NormalizeValue(newValue);
            if (string.Equals(normalizedOldValue, normalizedNewValue, StringComparison.Ordinal))
            {
                continue;
            }

            var displayName = ResolveFieldName(fieldName, fieldLabels);
            var isSensitive = IsSensitiveField(fieldName) || IsSensitiveField(displayName);
            changes.Add(new AuditChangeDiff(
                displayName,
                MaskValue(normalizedOldValue, isSensitive),
                MaskValue(normalizedNewValue, isSensitive)));
        }

        return changes;
    }

    public static string FormatSummary(IEnumerable<AuditChangeDiff> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return string.Join(
            "; ",
            changes.Select(change =>
                $"{change.FieldName}: было {FormatDisplayValue(change.OldValue)}, стало {FormatDisplayValue(change.NewValue)}"));
    }

    private static string ResolveFieldName(string fieldName, IReadOnlyDictionary<string, string>? fieldLabels)
    {
        if (fieldLabels is not null &&
            fieldLabels.TryGetValue(fieldName, out var label) &&
            !string.IsNullOrWhiteSpace(label))
        {
            return label.Trim();
        }

        return fieldName;
    }

    private static string? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
            double number => number.ToString("0.##", CultureInfo.InvariantCulture),
            float number => number.ToString("0.##", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim()
        };
    }

    private static string? MaskValue(string? value, bool isSensitive)
    {
        if (value is null)
        {
            return null;
        }

        return isSensitive ? "[секрет скрыт]" : AuditTextMasker.Mask(value);
    }

    private static string FormatDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(пусто)" : value;
    }

    private static bool IsSensitiveField(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var normalized = fieldName.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return SensitiveFieldNameParts.Any(part => normalized.Contains(part, StringComparison.Ordinal));
    }
}
