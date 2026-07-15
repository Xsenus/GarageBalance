namespace GarageBalance.Api.Tests.Deployment;

public sealed class DiagnosticLoggingDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void DockerCompose_PersistsBoundedDiagnosticLogsOutsideApiContainer()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var envExample = File.ReadAllText(Path.Combine(RepositoryRoot, ".env.example"));
        var appSettings = File.ReadAllText(Path.Combine(RepositoryRoot, "backend", "GarageBalance.Api", "appsettings.json"));

        Assert.Contains("${LOG_HOST_PATH:-./logs}:/logs", compose, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogging__Enabled", compose, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogging__RetentionDays", compose, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogging__PackageMaxSizeMb", compose, StringComparison.Ordinal);
        Assert.Contains("LOG_HOST_PATH=./logs", envExample, StringComparison.Ordinal);
        Assert.Contains("DIAGNOSTIC_LOGGING_RETENTION_DAYS=14", envExample, StringComparison.Ordinal);
        Assert.Contains("\"DiagnosticLogging\"", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"Directory\": \"logs\"", appSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void VpsDeployment_KeepsDiagnosticDirectoryAcrossReleaseSwaps()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("DIAGNOSTIC_LOG_DIR=\"${APP_ROOT}/logs\"", script, StringComparison.Ordinal);
        Assert.Contains("install -d -o \"${APP_USER}\" -g \"${APP_GROUP}\" -m 750 \"$DIAGNOSTIC_LOG_DIR\"", script, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogging__Directory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf \"$DIAGNOSTIC_LOG_DIR\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticArtifactsStayIgnoredAndOperationsAreDocumented()
    {
        var gitIgnore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "diagnostic-logging-guide.md"));

        Assert.Contains("logs/", gitIgnore, StringComparison.Ordinal);
        Assert.Contains("Настройки` → `Диагностика", guide, StringComparison.Ordinal);
        Assert.Contains("база PostgreSQL", guide, StringComparison.Ordinal);
        Assert.Contains("не включаются", guide, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
