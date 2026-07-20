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

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -o /app/publish --no-restore", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENV ASPNETCORE_URLS=http://+:8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("https://apt.postgresql.org/pub/repos/apt", dockerfile, StringComparison.Ordinal);
        Assert.Contains("postgresql-client-17", dockerfile, StringComparison.Ordinal);
        Assert.Contains("rm -rf /var/lib/apt/lists/*", dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 8080", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"GarageBalance.Api.dll\"]", dockerfile, StringComparison.Ordinal);
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
