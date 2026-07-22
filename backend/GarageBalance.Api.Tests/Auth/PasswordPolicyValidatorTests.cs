using GarageBalance.Api.Application.Auth;

namespace GarageBalance.Api.Tests.Auth;

public sealed class PasswordPolicyValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("        ")]
    [InlineData("1234567")]
    public void Validate_RejectsBlankOrShortPasswords(string password)
    {
        var validator = new PasswordPolicyValidator();

        var result = validator.Validate(password);

        Assert.False(result.Succeeded);
        Assert.Equal("Пароль должен быть не короче 8 символов.", result.ErrorMessage);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("12345678")]
    [InlineData("пароль12")]
    public void Validate_AcceptsAnyNonBlankPasswordWithMinimumLength(string password)
    {
        var validator = new PasswordPolicyValidator();

        var result = validator.Validate(password);

        Assert.True(result.Succeeded);
    }
}
