using GarageBalance.Api.Application.Auth;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PasswordPolicyValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short1A")]
    [InlineData("password")]
    [InlineData("PASSWORD1")]
    [InlineData("Password")]
    public void Validate_RejectsWeakPasswords(string password)
    {
        var validator = new PasswordPolicyValidator();

        var result = validator.Validate(password);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void Validate_AcceptsPasswordWithUppercaseLowercaseAndDigit()
    {
        var validator = new PasswordPolicyValidator();

        var result = validator.Validate("StrongPass123");

        Assert.True(result.Succeeded);
    }
}
