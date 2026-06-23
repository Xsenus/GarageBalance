using GarageBalance.Api.Application.Auth;
using Microsoft.Extensions.Options;

namespace GarageBalance.Api.Tests.Auth;

public sealed class JwtOptionsValidatorTests
{
    [Fact]
    public void Validate_AllowsDevelopmentPlaceholderKeyOnlyInDevelopment()
    {
        var validator = new JwtOptionsValidator("Development");

        var result = validator.Validate(null, new JwtOptions());

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_RejectsPlaceholderKeyOutsideDevelopment()
    {
        var validator = new JwtOptionsValidator("Production");

        var result = validator.Validate(null, new JwtOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("deployment secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short-key")]
    public void Validate_RejectsMissingOrShortSigningKey(string signingKey)
    {
        var validator = new JwtOptionsValidator("Production");

        var result = validator.Validate(null, new JwtOptions { SigningKey = signingKey });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("SigningKey", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowIfInvalid_AllowsStrongDeploymentSecret()
    {
        var options = new JwtOptions
        {
            SigningKey = "prod-secret-from-vault-2026-06-strong-64-bytes-value",
            AccessTokenMinutes = 30
        };

        JwtOptionsValidator.ThrowIfInvalid(options, "Production");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void Validate_RejectsInvalidAccessTokenLifetime(int accessTokenMinutes)
    {
        var validator = new JwtOptionsValidator("Production");

        var result = validator.Validate(null, new JwtOptions
        {
            SigningKey = "prod-secret-from-vault-2026-06-strong-64-bytes-value",
            AccessTokenMinutes = accessTokenMinutes
        });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("AccessTokenMinutes", StringComparison.Ordinal));
    }
}
