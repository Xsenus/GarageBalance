namespace GarageBalance.Api.Domain.Dictionaries;

public static class AccountingTypeCodePolicy
{
    private static readonly HashSet<string> ReservedIncomeCodes = new(StringComparer.Ordinal)
    {
        "water",
        "trash",
        "electricity",
        "membership",
        "target",
        "entry",
        "connection",
        "outdoor_lighting",
        "penalty",
        "notice",
        "fee_campaign",
        "other_payments",
        "other_income",
        "debt_transfer"
    };

    private static readonly HashSet<string> ReservedExpenseCodes = new(StringComparer.Ordinal)
    {
        "electricity",
        "trash_removal",
        "water_supply",
        "bank",
        "legal",
        "salary",
        "other",
        "penalty"
    };

    public static string? Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static bool IsValid(string? normalizedCode) =>
        normalizedCode is null ||
        (normalizedCode.Length <= 80 &&
         normalizedCode[0] is >= 'a' and <= 'z' &&
         normalizedCode.All(character =>
             character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'));

    public static bool IsReservedIncomeCode(string? normalizedCode) =>
        normalizedCode is not null && ReservedIncomeCodes.Contains(normalizedCode);

    public static bool IsReservedExpenseCode(string? normalizedCode) =>
        normalizedCode is not null && ReservedExpenseCodes.Contains(normalizedCode);
}
