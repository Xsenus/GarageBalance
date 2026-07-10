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

    [Fact]
    public void LocalPcChecklistCoversNoDomainSecretsBackupsDockerAndRollback()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "local-pc-install-checklist.md"));

        Assert.Contains("http://127.0.0.1:5173", document, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:5080/health", document, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Backups", document, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Config\\garagebalance.local.env", document, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", document, StringComparison.Ordinal);
        Assert.Contains("JWT_SIGNING_KEY", document, StringComparison.Ordinal);
        Assert.Contains("docker compose up --build -d", document, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_BIND_ADDRESS=127.0.0.1", document, StringComparison.Ordinal);
        Assert.Contains("API_BIND_ADDRESS=127.0.0.1", document, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_BIND_ADDRESS=127.0.0.1", document, StringComparison.Ordinal);
        Assert.Contains("BACKUP_HOST_PATH=./backups", document, StringComparison.Ordinal);
        Assert.Contains("check-local-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef database update", document, StringComparison.Ordinal);
        Assert.Contains("VITE_API_BASE_URL=\"http://127.0.0.1:5080\"", document, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("Условия финального закрытия локальной установки", document, StringComparison.Ordinal);
        Assert.Contains("docker compose config", document, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", document, StringComparison.Ordinal);
        Assert.Contains("Миграции применены к чистой локальной PostgreSQL", document, StringComparison.Ordinal);
        Assert.Contains("Не открывать порты", document, StringComparison.Ordinal);
        Assert.Contains("Rollback", document, StringComparison.Ordinal);
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
