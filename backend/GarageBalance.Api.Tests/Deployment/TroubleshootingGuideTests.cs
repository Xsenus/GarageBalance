namespace GarageBalance.Api.Tests.Deployment;

public sealed class TroubleshootingGuideTests
{
    [Fact]
    public void TroubleshootingGuideCoversOperationalFailuresAndSafeDiagnostics()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "troubleshooting-guide.md"));

        Assert.Contains("curl -fsS http://127.0.0.1:5080/health", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS https://sgk.blagodaty.ru/health", document, StringComparison.Ordinal);
        Assert.Contains("docker compose logs --tail=200 api", document, StringComparison.Ordinal);
        Assert.Contains("journalctl -u garagebalance-staging.service -n 200 --no-pager", document, StringComparison.Ordinal);
        Assert.Contains("nginx -t", document, StringComparison.Ordinal);
        Assert.Contains("pg_isready", document, StringComparison.Ordinal);
        Assert.Contains("psql --host=127.0.0.1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", document, StringComparison.Ordinal);
        Assert.Contains("dry-run", document, StringComparison.Ordinal);
        Assert.Contains("ACE-драйвер", document, StringComparison.Ordinal);
        Assert.Contains("all", document, StringComparison.Ordinal);
        Assert.Contains("accruals", document, StringComparison.Ordinal);
        Assert.Contains("payments", document, StringComparison.Ordinal);
        Assert.Contains("GET /api/app-releases", document, StringComparison.Ordinal);
        Assert.Contains("auth.login_rate_limited", document, StringComparison.Ordinal);
        Assert.Contains("1C Fresh", document, StringComparison.Ordinal);
        Assert.Contains("Принтер", document, StringComparison.Ordinal);
        Assert.Contains("connection string", document, StringComparison.Ordinal);
        Assert.Contains(".pgdump", document, StringComparison.Ordinal);
        Assert.Contains("roadmap history", document, StringComparison.Ordinal);
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
