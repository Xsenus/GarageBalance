using System.Text.Json;
using GarageBalance.Api.Application.Import;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class VpsPerformanceConfigurationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void NginxConfiguration_BoundsConnectionsAndUsesPersistentUpstream()
    {
        var nginx = ReadDeploymentFile("garagebalance-staging.nginx.conf");

        Assert.Contains("keepalive 16;", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_set_header Connection \"\";", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_connect_timeout 3s;", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_read_timeout 60s;", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_next_upstream off;", nginx, StringComparison.Ordinal);
        Assert.Contains("limit_req_zone $binary_remote_addr", nginx, StringComparison.Ordinal);
        Assert.Contains("client_header_timeout 10s;", nginx, StringComparison.Ordinal);
        Assert.Contains("client_body_timeout 30s;", nginx, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportUploadLimits_AreAlignedAcrossClientApiAndNginx()
    {
        var nginx = ReadDeploymentFile("garagebalance-staging.nginx.conf");
        using var appSettings = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "backend",
            "GarageBalance.Api",
            "appsettings.json")));
        var configuredMaximum = appSettings.RootElement
            .GetProperty("ImportProcessing")
            .GetProperty("MaximumFileSizeMegabytes")
            .GetInt32();
        var frontendLimits = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "frontend",
            "src",
            "features",
            "import",
            "importFileLimits.ts"));

        Assert.Equal(ImportFileLimits.MaximumFileSizeMegabytes, configuredMaximum);
        Assert.Contains(
            $"maximumAccessImportFileSizeMegabytes = {ImportFileLimits.MaximumFileSizeMegabytes}",
            frontendLimits,
            StringComparison.Ordinal);
        Assert.Contains("client_max_body_size 51m;", nginx, StringComparison.Ordinal);
        Assert.Equal(51L * 1024L * 1024L, ImportFileLimits.MultipartRequestSizeBytes);
    }

    [Fact]
    public void NginxConfiguration_CompressesAndCachesHashedAssets()
    {
        var nginx = ReadDeploymentFile("garagebalance-staging.nginx.conf");

        Assert.Contains("listen 443 ssl http2;", nginx, StringComparison.Ordinal);
        Assert.Contains("gzip on;", nginx, StringComparison.Ordinal);
        Assert.Contains("gzip_vary on;", nginx, StringComparison.Ordinal);
        Assert.Contains("gzip_proxied any;", nginx, StringComparison.Ordinal);
        Assert.Contains("public, max-age=2592000, immutable", nginx, StringComparison.Ordinal);
        Assert.Contains("no-store, no-cache, must-revalidate, max-age=0", nginx, StringComparison.Ordinal);
        Assert.Contains("add_header X-Request-ID $request_id always;", nginx, StringComparison.Ordinal);
        Assert.DoesNotContain("gzip off;", nginx, StringComparison.Ordinal);
        Assert.DoesNotContain("no-transform", nginx, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemdConfiguration_BoundsResourcesAndRestartsOnlyAfterFailure()
    {
        var service = ReadDeploymentFile("garagebalance-staging.service");

        Assert.Contains("Restart=on-failure", service, StringComparison.Ordinal);
        Assert.Contains("MemoryHigh=900M", service, StringComparison.Ordinal);
        Assert.Contains("MemoryMax=1200M", service, StringComparison.Ordinal);
        Assert.Contains("CPUQuota=150%", service, StringComparison.Ordinal);
        Assert.Contains("TasksMax=512", service, StringComparison.Ordinal);
        Assert.Contains("ProtectSystem=strict", service, StringComparison.Ordinal);
        Assert.Contains("NoNewPrivileges=true", service, StringComparison.Ordinal);
    }

    [Fact]
    public void WatchdogsAndLogs_AreBoundedAndDoNotRestartAfterOneFailure()
    {
        var healthScript = ReadScript("garagebalance-healthcheck.sh");
        var performanceScript = ReadScript("garagebalance-performance-check.sh");
        var logrotate = ReadDeploymentFile("garagebalance.logrotate");

        Assert.Contains("FAILURE_LIMIT=3", healthScript, StringComparison.Ordinal);
        Assert.Contains("/health/ready", healthScript, StringComparison.Ordinal);
        Assert.Contains("--noproxy 127.0.0.1", healthScript, StringComparison.Ordinal);
        Assert.Contains("Host: sgk.blagodaty.ru", healthScript, StringComparison.Ordinal);
        Assert.Contains("systemctl try-restart garagebalance-staging.service", healthScript, StringComparison.Ordinal);
        Assert.Contains("SAMPLE_LIMIT=1000", performanceScript, StringComparison.Ordinal);
        Assert.Contains("P95_LIMIT_SECONDS=\"1.500\"", performanceScript, StringComparison.Ordinal);
        Assert.Contains("field_index", performanceScript, StringComparison.Ordinal);
        Assert.DoesNotContain("for (index =", performanceScript, StringComparison.Ordinal);
        Assert.Contains("serverErrors=", performanceScript, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 1", performanceScript, StringComparison.Ordinal);
        Assert.Contains(
            "LOG_FILE=\"/var/log/garagebalance-nginx/garagebalance-staging-timing.log\"",
            performanceScript,
            StringComparison.Ordinal);
        Assert.Contains("/var/log/garagebalance-nginx/garagebalance-staging-timing.log", logrotate, StringComparison.Ordinal);
        Assert.Contains("/var/log/garagebalance-nginx/garagebalance-staging-error.log", logrotate, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/log/nginx/", logrotate, StringComparison.Ordinal);
        Assert.Contains("rotate 14", logrotate, StringComparison.Ordinal);
        Assert.Contains("maxsize 20M", logrotate, StringComparison.Ordinal);
        Assert.Contains("compress", logrotate, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_ValidatesHealthAndRollsBackFailedConfiguration()
    {
        var installer = ReadScript("install-vps-performance-configuration.sh");

        Assert.Contains("trap rollback EXIT", installer, StringComparison.Ordinal);
        Assert.Contains("install -d -o garagebalance -g garagebalance", installer, StringComparison.Ordinal);
        Assert.Contains("install -d -o www-data -g adm -m 0750 /var/log/garagebalance-nginx", installer, StringComparison.Ordinal);
        Assert.Contains("enabled_site_is_regular", installer, StringComparison.Ordinal);
        Assert.Contains("enabled_site_backup", installer, StringComparison.Ordinal);
        Assert.Contains("/etc/nginx/config-backups", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("sites-enabled/garagebalance-staging.backup-", installer, StringComparison.Ordinal);
        Assert.Contains("\"$enabled_site_target\"", installer, StringComparison.Ordinal);
        Assert.Contains("ln -s \"$site_target\" \"$enabled_site_target\"", installer, StringComparison.Ordinal);
        Assert.Contains("nginx -t", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl reload nginx", installer, StringComparison.Ordinal);
        Assert.True(
            installer.IndexOf("if (( health_ready == 0 ))", StringComparison.Ordinal) <
            installer.IndexOf("systemctl enable --now garagebalance-healthcheck.timer", StringComparison.Ordinal));
        Assert.Contains("for _ in {1..30}", installer, StringComparison.Ordinal);
        Assert.Contains("GarageBalance did not become healthy within 30 seconds.", installer, StringComparison.Ordinal);
        Assert.Contains("--noproxy 127.0.0.1", installer, StringComparison.Ordinal);
        Assert.Contains("Host: sgk.blagodaty.ru", installer, StringComparison.Ordinal);
        Assert.Contains("logrotate --debug /etc/logrotate.conf", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl start logrotate.service", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl reset-failed logrotate.service", installer, StringComparison.Ordinal);
        Assert.Contains("configuration were rolled back", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void TroubleshootingRunbook_CorrelatesBrowserNginxBackendAndDatabase()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "troubleshooting-guide.md"));

        Assert.Contains("Runbook «раздел завис»", guide, StringComparison.Ordinal);
        Assert.Contains("X-Request-ID", guide, StringComparison.Ordinal);
        Assert.Contains("request_time", guide, StringComparison.Ordinal);
        Assert.Contains("upstream_response_time", guide, StringComparison.Ordinal);
        Assert.Contains("SlowDatabaseCommand", guide, StringComparison.Ordinal);
        Assert.Contains("pg_stat_activity", guide, StringComparison.Ordinal);
        Assert.Contains("garagebalance-performance-check.timer", guide, StringComparison.Ordinal);
    }

    private static string ReadDeploymentFile(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "deployment", fileName));

    private static string ReadScript(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", fileName));

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
