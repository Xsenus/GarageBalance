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

        return PasswordPolicyResult.Success();
    }
}
