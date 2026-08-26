namespace GarageBalance.Api.Tests.Deployment;

public sealed class StagingCodexCleanupDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CleanupWorkflow_RequiresExactConfirmationAndUsesProtectedVpsCommand()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "cleanup-staging-codex-records.yml"));

        Assert.Contains("inputs.confirmation == 'PURGE GARAGEBALANCE STAGING CODEX'", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-deploy-apply cleanup-codex-records", workflow, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru/health/ready", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupScript_BackupsAndRestoreChecksBeforeScopedTransaction()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "infrastructure",
            "scripts",
            "cleanup-staging-codex-records.sh"));

        Assert.Contains("EXPECTED_CONFIRMATION=\"PURGE GARAGEBALANCE STAGING CODEX\"", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf("pg_dump --format=custom", StringComparison.Ordinal) < script.IndexOf("BEGIN;", StringComparison.Ordinal));
        Assert.Contains("pg_restore", script, StringComparison.Ordinal);
        Assert.Contains("Safety limit exceeded", script, StringComparison.Ordinal);
        Assert.Contains("protected payments or payouts; cleanup cancelled", script, StringComparison.Ordinal);
        Assert.Contains("payment allocations; cleanup cancelled", script, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM meter_readings", script, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM meter_devices", script, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM accruals", script, StringComparison.Ordinal);
        Assert.Contains("No Codex acceptance records were found", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeployPackage_InstallsCleanupScript()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "deploy-staging.yml"));
        var applyScript = File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("infrastructure/scripts/cleanup-staging-codex-records.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-cleanup-codex-records", applyScript, StringComparison.Ordinal);
        Assert.Contains("cleanup-codex-records", applyScript, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GarageBalance.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
