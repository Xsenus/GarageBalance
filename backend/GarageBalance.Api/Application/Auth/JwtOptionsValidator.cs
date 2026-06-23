using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Application.Auth;

public sealed class JwtOptionsValidator(string environmentName) : IValidateOptions<JwtOptions>
{
    public const int MinimumSigningKeyBytes = 32;

    private static readonly string[] UnsafeSigningKeyMarkers =
    [
        "change-this",
        "development-key",
        "example",
        "sample",
        "password"
    ];

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = GetFailures(options, environmentName).ToArray();
        return failures.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    public static void ThrowIfInvalid(JwtOptions options, string environmentName)
    {
        var failures = GetFailures(options, environmentName).ToArray();
        if (failures.Length > 0)
        {
            throw new OptionsValidationException(JwtOptions.SectionName, typeof(JwtOptions), failures);
        }
    }

    private static IEnumerable<string> GetFailures(JwtOptions options, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            yield return "Jwt:Issuer is required.";
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            yield return "Jwt:Audience is required.";
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            yield return "Jwt:SigningKey is required.";
        }
        else
        {
            var keyBytes = System.Text.Encoding.UTF8.GetByteCount(options.SigningKey);
            if (keyBytes < MinimumSigningKeyBytes)
            {
                yield return $"Jwt:SigningKey must be at least {MinimumSigningKeyBytes} UTF-8 bytes.";
            }

            if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
                UnsafeSigningKeyMarkers.Any(marker => options.SigningKey.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                yield return "Jwt:SigningKey must be replaced with a deployment secret outside Development.";
            }
        }

        if (options.AccessTokenMinutes is < 5 or > 1440)
        {
            yield return "Jwt:AccessTokenMinutes must be between 5 and 1440.";
        }
    }
}
