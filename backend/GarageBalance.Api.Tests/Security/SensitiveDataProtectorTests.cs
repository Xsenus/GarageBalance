using GarageBalance.Api.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace GarageBalance.Api.Tests.Security;

public sealed class SensitiveDataProtectorTests
{
    [Fact]
    public void Protect_ReturnsVersionedCiphertextWithoutPlaintext()
    {
        var protector = CreateProtector();
        const string secret = "one-c-fresh-refresh-token-secret";

        var encrypted = protector.Protect(secret, "OneCFresh.Token");

        Assert.StartsWith(DataProtectionSensitiveDataProtector.ProtectedValuePrefix, encrypted, StringComparison.Ordinal);
        Assert.NotEqual(secret, encrypted);
        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
    }

    [Fact]
    public void Unprotect_RestoresProtectedValueWithSamePurpose()
    {
        var protector = CreateProtector();
        const string secret = "fiscal-printer-token";

        var encrypted = protector.Protect(secret, "FiscalPrinter.Token");

        Assert.Equal(secret, protector.Unprotect(encrypted, "FiscalPrinter.Token"));
    }

    [Fact]
    public void Unprotect_RejectsDifferentPurpose()
    {
        var protector = CreateProtector();
        var encrypted = protector.Protect("integration-secret", "OneCFresh.Token");

        Assert.ThrowsAny<Exception>(() => protector.Unprotect(encrypted, "FiscalPrinter.Token"));
    }

    [Fact]
    public void Unprotect_RejectsPlaintextWithoutProtectedPrefix()
    {
        var protector = CreateProtector();

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Unprotect("plain-secret", "OneCFresh.Token"));
        Assert.DoesNotContain("plain-secret", exception.Message, StringComparison.Ordinal);
    }

    private static DataProtectionSensitiveDataProtector CreateProtector()
    {
        var provider = new EphemeralDataProtectionProvider();
        return new DataProtectionSensitiveDataProtector(provider);
    }
}
