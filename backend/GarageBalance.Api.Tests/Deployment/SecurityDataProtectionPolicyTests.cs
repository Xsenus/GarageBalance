namespace GarageBalance.Api.Tests.Deployment;

public sealed class SecurityDataProtectionPolicyTests
{
    [Fact]
    public void SecurityDataProtectionPolicyCoversSensitiveFieldsRetentionLogsAndEncryption()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "security-data-protection.md"));

        Assert.Contains("Owner.LastName", document, StringComparison.Ordinal);
        Assert.Contains("Owner.Phone", document, StringComparison.Ordinal);
        Assert.Contains("Owner.Address", document, StringComparison.Ordinal);
        Assert.Contains("AppUser.Email", document, StringComparison.Ordinal);
        Assert.Contains("AppUser.PasswordHash", document, StringComparison.Ordinal);
        Assert.Contains("Supplier.ContactPerson", document, StringComparison.Ordinal);
        Assert.Contains("FinancialOperation.Amount", document, StringComparison.Ordinal);
        Assert.Contains("AccessImportRun.OriginalFileName", document, StringComparison.Ordinal);
        Assert.Contains("ContentSha256", document, StringComparison.Ordinal);
        Assert.Contains("ReportJson", document, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey", document, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:Default", document, StringComparison.Ordinal);
        Assert.Contains("OneCFresh", document, StringComparison.Ordinal);
        Assert.Contains("private-imports/", document, StringComparison.Ordinal);
        Assert.Contains("imports/private/", document, StringComparison.Ordinal);
        Assert.Contains("imports/raw/", document, StringComparison.Ordinal);
        Assert.Contains("30 календарных дней", document, StringComparison.Ordinal);
        Assert.Contains("Quarantine/error bucket", document, StringComparison.Ordinal);
        Assert.Contains("Bearer token", document, StringComparison.Ordinal);
        Assert.Contains("CSV-экспорт audit", document, StringComparison.Ordinal);
        Assert.Contains("зашифрованном виде", document, StringComparison.Ordinal);
        Assert.Contains("Rotation ключей", document, StringComparison.Ordinal);
        Assert.Contains("verify-package-privacy.ps1", document, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
