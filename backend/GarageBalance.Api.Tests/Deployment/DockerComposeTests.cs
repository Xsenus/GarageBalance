namespace GarageBalance.Api.Tests.Deployment;

public sealed class DockerComposeTests
{
    [Fact]
    public void ComposeDefinesServiceHealthChecksAndStartupOrder()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docker-compose.yml"));

        Assert.Contains("postgres:", compose, StringComparison.Ordinal);
        Assert.Contains("pg_isready -U ${POSTGRES_USER:-garagebalance} -d ${POSTGRES_DB:-garagebalance}", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);

        Assert.Contains("api:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: backend/GarageBalance.Api/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS: http://+:8080", compose, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:8080/health > /dev/null", compose, StringComparison.Ordinal);
        Assert.Contains("\"${API_PORT:-5080}:8080\"", compose, StringComparison.Ordinal);

        Assert.Contains("frontend:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: frontend/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("wget -q --spider http://127.0.0.1/", compose, StringComparison.Ordinal);
        Assert.Contains("\"${FRONTEND_PORT:-5173}:80\"", compose, StringComparison.Ordinal);
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
