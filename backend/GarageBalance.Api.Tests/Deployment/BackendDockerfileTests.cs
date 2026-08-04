using System.Security.Cryptography;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackendDockerfileTests
{
    [Fact]
    public void DockerfileUsesNet10ReleasePublishAndStableRuntimePort()
    {
        var dockerfile = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "backend",
            "GarageBalance.Api",
            "Dockerfile"));

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY backend/GarageBalance.Api/packages.lock.json", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj --locked-mode", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -o /app/publish --no-restore", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:f1126d438ccc359f51cc6d4701a8deae513856cf10f5fe645d29ea6403dcac6b", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENV ASPNETCORE_URLS=http://+:8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY infrastructure/deployment/postgresql-archive-keyring.asc", dockerfile, StringComparison.Ordinal);
        Assert.Contains("sha256sum --check --strict", dockerfile, StringComparison.Ordinal);
        Assert.Contains("https://apt.postgresql.org/pub/repos/apt", dockerfile, StringComparison.Ordinal);
        Assert.Contains("postgresql-client-17=17.10-1.pgdg24.04+1", dockerfile, StringComparison.Ordinal);
        Assert.Contains("postgresql-client-common=293.pgdg24.04+1", dockerfile, StringComparison.Ordinal);
        Assert.Contains("libpq5=18.4-1.pgdg24.04+1", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("curl", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj\n", dockerfile, StringComparison.Ordinal);
        Assert.Contains("rm -rf /var/lib/apt/lists/*", dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"GarageBalance.Api.dll\"]", dockerfile, StringComparison.Ordinal);

        var keyPath = Path.Combine(
            FindRepositoryRoot(),
            "infrastructure",
            "deployment",
            "postgresql-archive-keyring.asc");
        var keyHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(keyPath)));

        Assert.Equal("0144068502a1eddd2a0280ede10ef607d1ec592ce819940991203941564e8e76", keyHash);
        Assert.Contains(keyHash, dockerfile, StringComparison.Ordinal);
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
