using GarageBalance.Api.Application.Dictionaries;

namespace GarageBalance.Api.Tests.Dictionaries;

public sealed class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+7 (913) 123-45-67", "+7 (913) 123-45-67")]
    [InlineData("8 913 123 45 67", "+7 (913) 123-45-67")]
    [InlineData("79131234567", "+7 (913) 123-45-67")]
    [InlineData("9131234567", "+7 (913) 123-45-67")]
    [InlineData("  +7-913-123-45-67  ", "+7 (913) 123-45-67")]
    public void TryNormalize_RecognizedRussianNumber_ReturnsCanonicalFormat(string source, string expected)
    {
        var succeeded = PhoneNumberNormalizer.TryNormalize(source, out var normalized);

        Assert.True(succeeded);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_EmptyValue_ReturnsNull(string? source)
    {
        var succeeded = PhoneNumberNormalizer.TryNormalize(source, out var normalized);

        Assert.True(succeeded);
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("+7 913")]
    [InlineData("+7 913 123-45-678")]
    [InlineData("+1 202 555-01-23")]
    [InlineData("1202555012")]
    [InlineData("телефон +7 913 123-45-67")]
    public void TryNormalize_InvalidValue_ReturnsFalse(string source)
    {
        var succeeded = PhoneNumberNormalizer.TryNormalize(source, out var normalized);

        Assert.False(succeeded);
        Assert.Null(normalized);
    }
}
