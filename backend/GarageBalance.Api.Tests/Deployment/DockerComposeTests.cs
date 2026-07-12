namespace GarageBalance.Api.Tests.Deployment;

public sealed class DockerComposeTests
{
    [Fact]
    public void ComposeDefinesServiceHealthChecksAndStartupOrder()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docker-compose.yml"));

        Assert.Contains("postgres:", compose, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env before running docker compose}", compose, StringComparison.Ordinal);
        Assert.Contains("pg_isready -U ${POSTGRES_USER:-garagebalance} -d ${POSTGRES_DB:-garagebalance}", compose, StringComparison.Ordinal);
        Assert.Contains("\"${POSTGRES_BIND_ADDRESS:-127.0.0.1}:${POSTGRES_PORT:-5432}:5432\"", compose, StringComparison.Ordinal);
        Assert.Contains("${BACKUP_HOST_PATH:-./backups}:/backups", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);

        Assert.Contains("api:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: backend/GarageBalance.Api/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS: http://+:8080", compose, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey: ${JWT_SIGNING_KEY:?Set JWT_SIGNING_KEY in .env before running docker compose}", compose, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath: ${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", compose, StringComparison.Ordinal);
        Assert.Contains("Cors__AllowedOrigins__0: ${FRONTEND_ORIGIN:-http://127.0.0.1:5173}", compose, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:8080/health > /dev/null", compose, StringComparison.Ordinal);
        Assert.Contains("\"${API_BIND_ADDRESS:-127.0.0.1}:${API_PORT:-5080}:8080\"", compose, StringComparison.Ordinal);

        Assert.Contains("frontend:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: frontend/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("wget -q --spider http://127.0.0.1/", compose, StringComparison.Ordinal);
        Assert.Contains("\"${FRONTEND_BIND_ADDRESS:-127.0.0.1}:${FRONTEND_PORT:-5173}:80\"", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:", compose, StringComparison.Ordinal);
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
