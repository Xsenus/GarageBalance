namespace GarageBalance.Api.Tests.Deployment;

public sealed class PackagePrivacyScriptTests
{
    [Fact]
    public void VerifyPackagePrivacyScriptScansGitCandidatesAndBlocksSensitiveFiles()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "infrastructure", "scripts", "verify-package-privacy.ps1"));

        Assert.Contains("git rev-parse --show-toplevel", script, StringComparison.Ordinal);
        Assert.Contains("git ls-files", script, StringComparison.Ordinal);
        Assert.Contains("git ls-files --others --exclude-standard", script, StringComparison.Ordinal);
        Assert.Contains("sensitivePathPatterns", script, StringComparison.Ordinal);
        Assert.Contains("\\.env\\.example", script, StringComparison.Ordinal);
        Assert.Contains("appsettings\\.Local\\.json", script, StringComparison.Ordinal);
        Assert.Contains("accdb", script, StringComparison.Ordinal);
        Assert.Contains("mdb", script, StringComparison.Ordinal);
        Assert.Contains("pgdump", script, StringComparison.Ordinal);
        Assert.Contains("sql\\.gz", script, StringComparison.Ordinal);
        Assert.Contains("artifacts", script, StringComparison.Ordinal);
        Assert.Contains("private-imports", script, StringComparison.Ordinal);
        Assert.Contains("imports/(private|raw)", script, StringComparison.Ordinal);
        Assert.Contains("Privacy check failed", script, StringComparison.Ordinal);
        Assert.Contains("privacyCheck=passed", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationReferencesPackagePrivacyScriptBeforePublication()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        var testingGuide = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "testing-guide.md"));

        Assert.Contains("verify-package-privacy.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("verify-package-privacy.ps1", testingGuide, StringComparison.Ordinal);
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
