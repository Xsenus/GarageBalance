namespace GarageBalance.Api.Tests.Deployment;

public sealed class VersionUpdateChecklistTests
{
    [Fact]
    public void VersionUpdateChecklistRequiresBackupHealthSmokeChecksAndRollback()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "version-update-checklist.md"));

        Assert.Contains("check-local-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("dotnet test", document, StringComparison.Ordinal);
        Assert.Contains("npm run test -- --runInBand", document, StringComparison.Ordinal);
        Assert.Contains("npm run build", document, StringComparison.Ordinal);
        Assert.Contains("npm run lint", document, StringComparison.Ordinal);
        Assert.Contains("dotnet format --verify-no-changes", document, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", document, StringComparison.Ordinal);
        Assert.Contains("docker compose up --build -d", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:5080/health", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS https://sgk.blagodaty.ru/health", document, StringComparison.Ordinal);
        Assert.Contains("GET /api/app-releases", document, StringComparison.Ordinal);
        Assert.Contains("Cache-Control: no-store", document, StringComparison.Ordinal);
        Assert.Contains("Rollback", document, StringComparison.Ordinal);
        Assert.Contains("git push", document, StringComparison.Ordinal);
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
