using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Auth;

public sealed class InitialAdministratorOptions
{
    public const string SectionName = "InitialAdministrator";

    public bool Enabled { get; init; }
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class InitialAdministratorOptionsValidator : IValidateOptions<InitialAdministratorOptions>
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public ValidateOptionsResult Validate(string? name, InitialAdministratorOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.Email) ||
            options.Email.Length > 320 ||
            !EmailValidator.IsValid(options.Email))
        {
            return ValidateOptionsResult.Fail("InitialAdministrator:Email must contain a valid email address up to 320 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.DisplayName) || options.DisplayName.Length > 200)
        {
            return ValidateOptionsResult.Fail("InitialAdministrator:DisplayName must contain from 1 to 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(options.Password) ||
            options.Password.Length is < PasswordPolicyValidator.MinimumLength or > 200 ||
            options.Password.StartsWith("__", StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail("InitialAdministrator:Password must contain from 8 to 200 characters and must not be a template placeholder.");
        }

        return ValidateOptionsResult.Success;
    }
}
