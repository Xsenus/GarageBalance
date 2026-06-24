namespace GarageBalance.Api.Application.Security;

public interface ISensitiveDataProtector
{
    string Protect(string plaintext, string purpose);

    string Unprotect(string protectedValue, string purpose);
}
