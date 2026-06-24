using GarageBalance.Api.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace GarageBalance.Api.Infrastructure.Security;

public sealed class DataProtectionSensitiveDataProtector(IDataProtectionProvider dataProtectionProvider) : ISensitiveDataProtector
{
    public const string ProtectedValuePrefix = "gb:protected:v1:";

    public string Protect(string plaintext, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var protector = dataProtectionProvider.CreateProtector(BuildPurpose(purpose));
        return ProtectedValuePrefix + protector.Protect(plaintext);
    }

    public string Unprotect(string protectedValue, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        if (!protectedValue.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Protected value has an unsupported format.");
        }

        var protector = dataProtectionProvider.CreateProtector(BuildPurpose(purpose));
        var payload = protectedValue[ProtectedValuePrefix.Length..];
        return protector.Unprotect(payload);
    }

    private static string BuildPurpose(string purpose)
    {
        return $"GarageBalance.SensitiveData.{purpose.Trim()}";
    }
}
