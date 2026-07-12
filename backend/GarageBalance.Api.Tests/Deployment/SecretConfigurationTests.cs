namespace GarageBalance.Api.Tests.Deployment;

public sealed class SecretConfigurationTests
{
    [Fact]
    public void ApiProjectAndDocumentationSupportUserSecretsEnvironmentAndPersistentDeploymentKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectText = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "GarageBalance.Api.csproj"));
        var readmeText = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        var envExampleText = File.ReadAllText(Path.Combine(repositoryRoot, ".env.example"));
        var composeText = File.ReadAllText(Path.Combine(repositoryRoot, "docker-compose.yml"));
        var localInstallText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "local-pc-install-checklist.md"));
        var vpsInstallText = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "vps-deployment-checklist.md"));

        Assert.Contains("<UserSecretsId>GarageBalance.Api-", projectText, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set \"Jwt:SigningKey\"", readmeText, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set \"ConnectionStrings:DefaultConnection\"", readmeText, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set \"DataProtection:KeysPath\"", readmeText, StringComparison.Ordinal);
        Assert.Contains("DATA_PROTECTION_KEYS_PATH=/var/lib/garagebalance/keys", envExampleText, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath: ${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", composeText, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", composeText, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath=C:\\GarageBalance\\Config\\DataProtectionKeys", localInstallText, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__DefaultConnection", vpsInstallText, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionStrings__Postgres", vpsInstallText, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath=/var/lib/garagebalance-staging/data-protection-keys", vpsInstallText, StringComparison.Ordinal);
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
