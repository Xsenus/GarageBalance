namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendDockerfileTests
{
    [Fact]
    public void DockerfileUsesNodeBuildStageNginxRuntimeAndSpaCachePolicy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dockerfile = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "Dockerfile"));
        var nginxConfig = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "nginx.conf"));

        Assert.Contains("FROM node:22-alpine AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY frontend/package*.json ./", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN npm ci", dockerfile, StringComparison.Ordinal);
        Assert.Contains("RUN npm run build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM nginx:1.27-alpine AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=build /app/dist /usr/share/nginx/html", dockerfile, StringComparison.Ordinal);
        Assert.Contains("EXPOSE 80", dockerfile, StringComparison.Ordinal);

        Assert.Contains("location /assets/", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("Cache-Control \"public, max-age=2592000, immutable\"", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("location = /index.html", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("Cache-Control \"no-store, no-cache, must-revalidate, max-age=0\" always", nginxConfig, StringComparison.Ordinal);
        Assert.Contains("try_files $uri $uri/ /index.html", nginxConfig, StringComparison.Ordinal);
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
