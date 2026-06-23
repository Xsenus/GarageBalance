namespace GarageBalance.Api.Application.Auth;

public sealed record PasswordPolicyResult(bool Succeeded, string? ErrorMessage)
{
    public static PasswordPolicyResult Success() => new(true, null);

    public static PasswordPolicyResult Failure(string message) => new(false, message);
}

public interface IPasswordPolicyValidator
{
    PasswordPolicyResult Validate(string password);
}

public sealed class PasswordPolicyValidator : IPasswordPolicyValidator
{
    public const int MinimumLength = 8;

    public PasswordPolicyResult Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            return PasswordPolicyResult.Failure($"Пароль должен быть не короче {MinimumLength} символов.");
        }

        if (!password.Any(char.IsUpper))
        {
            return PasswordPolicyResult.Failure("Пароль должен содержать хотя бы одну заглавную букву.");
        }

        if (!password.Any(char.IsLower))
        {
            return PasswordPolicyResult.Failure("Пароль должен содержать хотя бы одну строчную букву.");
        }

        if (!password.Any(char.IsDigit))
        {
            return PasswordPolicyResult.Failure("Пароль должен содержать хотя бы одну цифру.");
        }

        return PasswordPolicyResult.Success();
    }
}
