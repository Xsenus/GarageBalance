namespace GarageBalance.Api.Application.Dictionaries;

public static class PhoneNumberNormalizer
{
    public const string FormatHint = "+7 (999) 123-45-67";

    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Any(character => !IsAllowedCharacter(character)))
        {
            return false;
        }

        var digits = new string(trimmed.Where(character => character is >= '0' and <= '9').ToArray());
        var nationalNumber = digits switch
        {
            { Length: 10 } => digits,
            { Length: 11 } when digits[0] is '7' or '8' => digits[1..],
            _ => null
        };

        if (nationalNumber is null || nationalNumber[0] is not ('3' or '4' or '8' or '9'))
        {
            return false;
        }

        normalized = $"+7 ({nationalNumber[..3]}) {nationalNumber.Substring(3, 3)}-{nationalNumber.Substring(6, 2)}-{nationalNumber.Substring(8, 2)}";
        return true;
    }

    private static bool IsAllowedCharacter(char character) =>
        character is >= '0' and <= '9' or '+' or '(' or ')' or '-' || char.IsWhiteSpace(character);
}
