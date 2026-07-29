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
    public void VpsChecklist_UsesQueryFreeRequestTimingDiagnostics()
    {
        var checklist = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "vps-deployment-checklist.md"));
        var troubleshooting = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "troubleshooting-guide.md"));

        Assert.Contains("garagebalance_timing", checklist, StringComparison.Ordinal);
        Assert.Contains("uri=$uri", checklist, StringComparison.Ordinal);
        Assert.DoesNotContain("uri=$request_uri", checklist, StringComparison.Ordinal);
        Assert.Contains("Server-Timing: app;dur=", troubleshooting, StringComparison.Ordinal);
        Assert.Contains("SlowHttpRequest", troubleshooting, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestDiagnostics_CorrelateNginxBackendAndDatabaseWithoutSqlText()
    {
        var checklist = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "vps-deployment-checklist.md"));
        var troubleshooting = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "troubleshooting-guide.md"));
        var dockerNginx = File.ReadAllText(Path.Combine(RepositoryRoot, "frontend", "nginx.conf"));
        var interceptor = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "backend",
            "GarageBalance.Api",
            "Infrastructure",
            "Diagnostics",
            "DatabaseCommandPerformanceInterceptor.cs"));

        Assert.Contains("request_id=$request_id", checklist, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header X-Error-ID $request_id;", checklist, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header X-Error-ID $request_id;", dockerNginx, StringComparison.Ordinal);
        Assert.Contains("SlowDatabaseCommand", troubleshooting, StringComparison.Ordinal);
        Assert.Contains("FailedDatabaseCommand", troubleshooting, StringComparison.Ordinal);
        Assert.DoesNotContain(".CommandText", interceptor, StringComparison.Ordinal);
        Assert.DoesNotContain(".Parameters", interceptor, StringComparison.Ordinal);
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
