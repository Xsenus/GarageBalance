namespace GarageBalance.Api.Tests.Deployment;

public sealed class AdminOperationsGuideTests
{
    [Fact]
    public void AdminOperationsGuideCoversUsersRolesBackupsUpdatesHealthAuditAndLogs()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "admin-operations-guide.md"));

        Assert.Contains("administrator", document, StringComparison.Ordinal);
        Assert.Contains("accountant", document, StringComparison.Ordinal);
        Assert.Contains("operator", document, StringComparison.Ordinal);
        Assert.Contains("reports_viewer", document, StringComparison.Ordinal);
        Assert.Contains("users.manage", document, StringComparison.Ordinal);
        Assert.Contains("audit.read", document, StringComparison.Ordinal);
        Assert.Contains("GET /api/audit/events", document, StringComparison.Ordinal);
        Assert.Contains("GET /api/audit/events/export", document, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("register-local-backup-task.ps1", document, StringComparison.Ordinal);
        Assert.Contains("docs/version-update-checklist.md", document, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:5080/health", document, StringComparison.Ordinal);
        Assert.Contains("curl -fsS https://sgk.blagodaty.ru/health", document, StringComparison.Ordinal);
        Assert.Contains("docker compose logs --tail=200 api", document, StringComparison.Ordinal);
        Assert.Contains("journalctl -u garagebalance-staging.service -n 200 --no-pager", document, StringComparison.Ordinal);
        Assert.Contains("nginx -t", document, StringComparison.Ordinal);
        Assert.Contains("git push", document, StringComparison.Ordinal);
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
