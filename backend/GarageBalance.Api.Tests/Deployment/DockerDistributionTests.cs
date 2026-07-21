namespace GarageBalance.Api.Tests.Deployment;

public sealed class DockerDistributionTests
{
    [Fact]
    public void ReleaseComposeUsesVersionedImagesAndPersistentStorage()
    {
        var distribution = DistributionDirectory();
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));

        Assert.Contains("name: garagebalance", compose, StringComparison.Ordinal);
        Assert.Contains("ghcr.io/xsenus/garagebalance-api:${GARAGEBALANCE_VERSION", compose, StringComparison.Ordinal);
        Assert.Contains("ghcr.io/xsenus/garagebalance-frontend:${GARAGEBALANCE_VERSION", compose, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(compose, "pull_policy: never"));
        Assert.Contains("postgres-data:/var/lib/postgresql/data", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:${DATA_PROTECTION_KEYS_PATH", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("build:", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsCommandsGenerateSecretsLoadImagesAndProtectUpdates()
    {
        var distribution = DistributionDirectory();
        var common = File.ReadAllText(Path.Combine(distribution, "GarageBalance.Common.ps1"));
        var update = File.ReadAllText(Path.Combine(distribution, "update.ps1"));
        var diagnostics = File.ReadAllText(Path.Combine(distribution, "diagnostics.ps1"));
        var stop = File.ReadAllText(Path.Combine(distribution, "stop.ps1"));

        Assert.Contains("RandomNumberGenerator", common, StringComparison.Ordinal);
        Assert.Contains("WaitForExit($TimeoutSeconds * 1000)", common, StringComparison.Ordinal);
        Assert.Contains("$process.Kill()", common, StringComparison.Ordinal);
        Assert.Contains("docker", common, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"load\", \"--input\"", common, StringComparison.Ordinal);
        Assert.Contains("Wait-GarageBalanceHealth", common, StringComparison.Ordinal);
        Assert.Contains("backup.ps1", update, StringComparison.Ordinal);
        Assert.True(
            update.IndexOf("backup.ps1", StringComparison.Ordinal) < update.IndexOf("Import-GarageBalanceImages", StringComparison.Ordinal),
            "A verified backup must run before new images are imported.");
        Assert.DoesNotContain("GarageBalanceEnvFile", diagnostics.Split("$commands =", 2)[0], StringComparison.Ordinal);
        Assert.Contains("WaitForExit($TimeoutSeconds * 1000)", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Команда Docker прервана после тайм-аута", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Invoke-GarageBalanceComposeQuiet", File.ReadAllText(Path.Combine(distribution, "backup.ps1")), StringComparison.Ordinal);
        Assert.Contains("готовый backup-файл не найден или пуст", File.ReadAllText(Path.Combine(distribution, "backup.ps1")), StringComparison.Ordinal);
        Assert.DoesNotContain("-v", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowVerifiesBuildsAndPackagesAutonomousBundle()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-docker-release.yml"));

        Assert.Contains("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", workflow, StringComparison.Ordinal);
        Assert.Contains("check-docker-distribution.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("docker/build-push-action@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("docker save", workflow, StringComparison.Ordinal);
        Assert.Contains("GarageBalance-Docker-${VERSION}.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("sha256sum", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiImageUsesTheSamePostgreSqlMajorVersionAsReleaseCompose()
    {
        var root = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(root, "backend", "GarageBalance.Api", "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(root, "distribution", "docker", "docker-compose.yml"));

        Assert.Contains("postgresql-client-17", dockerfile, StringComparison.Ordinal);
        Assert.Contains("image: postgres:17-alpine", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void DistributionContainsOnlyTemplatesNotRealSecrets()
    {
        var distribution = DistributionDirectory();
        var environment = File.ReadAllText(Path.Combine(distribution, ".env.example"));

        Assert.False(File.Exists(Path.Combine(distribution, ".env")));
        Assert.Contains("POSTGRES_PASSWORD=__GENERATE__", environment, StringComparison.Ordinal);
        Assert.Contains("JWT_SIGNING_KEY=__GENERATE__", environment, StringComparison.Ordinal);
        Assert.Contains("GARAGEBALANCE_VERSION=__GARAGEBALANCE_VERSION__", environment, StringComparison.Ordinal);
        Assert.Equal("__GARAGEBALANCE_VERSION__", File.ReadAllText(Path.Combine(distribution, "release-version.txt")).Trim());
    }

    [Fact]
    public void WindowsLanGuideMatchesSupportedComposeSettingsAndSafeFirewallScope()
    {
        var root = FindRepositoryRoot();
        var distribution = DistributionDirectory();
        var guide = File.ReadAllText(Path.Combine(root, "docs", "docker-windows-lan-guide.md"));
        var bundleReadme = File.ReadAllText(Path.Combine(distribution, "README.txt"));
        var environment = File.ReadAllText(Path.Combine(distribution, ".env.example"));
        var compose = File.ReadAllText(Path.Combine(distribution, "docker-compose.yml"));

        Assert.Contains("FRONTEND_BIND_ADDRESS=0.0.0.0", guide, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_ORIGIN=http://192.168.1.50:8080", guide, StringComparison.Ordinal);
        Assert.Contains("API_BIND_ADDRESS=127.0.0.1", guide, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_BIND_ADDRESS=127.0.0.1", guide, StringComparison.Ordinal);
        Assert.Contains("New-NetFirewallRule", guide, StringComparison.Ordinal);
        Assert.Contains("-RemoteAddress LocalSubnet", guide, StringComparison.Ordinal);
        Assert.Contains("backup.cmd", guide, StringComparison.Ordinal);
        Assert.Contains("не копирует `.env`", guide, StringComparison.Ordinal);
        Assert.Contains("garagebalance_data-protection-keys", guide, StringComparison.Ordinal);
        Assert.Contains("docker-windows-lan-guide.md", bundleReadme, StringComparison.Ordinal);

        foreach (var variable in new[] { "FRONTEND_BIND_ADDRESS", "FRONTEND_PORT", "FRONTEND_ORIGIN", "API_BIND_ADDRESS", "POSTGRES_BIND_ADDRESS" })
        {
            Assert.Contains($"{variable}=", environment, StringComparison.Ordinal);
            Assert.Contains($"${{{variable}", compose, StringComparison.Ordinal);
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var position = 0;
        while ((position = value.IndexOf(search, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += search.Length;
        }

        return count;
    }

    private static string DistributionDirectory() =>
        Path.Combine(FindRepositoryRoot(), "distribution", "docker");

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
