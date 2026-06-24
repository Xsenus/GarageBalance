namespace GarageBalance.Api.Tests.Deployment;

public sealed class DeploymentDocumentationTests
{
    [Fact]
    public void VpsDeploymentChecklistCoversDomainTlsBackupsRollbackAndSmokeChecks()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "vps-deployment-checklist.md"));

        Assert.Contains("sgk.blagodaty.ru", document, StringComparison.Ordinal);
        Assert.Contains("/opt/garagebalance-staging", document, StringComparison.Ordinal);
        Assert.Contains("/etc/garagebalance-staging.env", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging.service", document, StringComparison.Ordinal);
        Assert.Contains("certbot --nginx -d sgk.blagodaty.ru", document, StringComparison.Ordinal);
        Assert.Contains("nginx -t", document, StringComparison.Ordinal);
        Assert.Contains("pg_dump", document, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", document, StringComparison.Ordinal);
        Assert.Contains("Cache-Control", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS https://sgk.blagodaty.ru/health", document, StringComparison.Ordinal);
        Assert.Contains("Rollback", document, StringComparison.Ordinal);
        Assert.Contains("Не коммитить", document, StringComparison.Ordinal);
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
