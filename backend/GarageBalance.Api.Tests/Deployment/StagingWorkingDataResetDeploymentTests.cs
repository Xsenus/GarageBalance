namespace GarageBalance.Api.Tests.Deployment;

public sealed class StagingWorkingDataResetDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void ResetScript_RequiresExactTargetAndVerifiedBackupWithAutomaticRestore()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "infrastructure",
            "scripts",
            "reset-staging-working-data.sh"));

        Assert.Contains("EXPECTED_CONFIRMATION=\"RESET GARAGEBALANCE STAGING\"", script, StringComparison.Ordinal);
        Assert.Contains("database_name\" == \"garagebalance_staging", script, StringComparison.Ordinal);
        Assert.Contains("pg_dump --format=custom", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore --exit-on-error", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore --clean --if-exists --exit-on-error", script, StringComparison.Ordinal);
        Assert.Contains("reason=archive-path", script, StringComparison.Ordinal);
        Assert.Contains("reason=archive-owner", script, StringComparison.Ordinal);
        Assert.Contains("systemctl stop \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("GarageBalance.ShowcaseSeed\" reset", script, StringComparison.Ordinal);
        Assert.Contains("/health/ready", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualWorkflow_RequiresExactConfirmationAndUsesInstalledResetHelper()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "reset-staging-working-data.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.confirmation == 'RESET GARAGEBALANCE STAGING'", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-deploy-apply reset-working-data", workflow, StringComparison.Ordinal);
        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: staging", workflow, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru/health/ready", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RegularDeployment_InstallsResetHelperButNeverRunsItAutomatically()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "deploy-staging.yml"));
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "infrastructure",
            "scripts",
            "vps-apply-release.sh"));

        Assert.Contains("infrastructure/scripts/reset-staging-working-data.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-reset-working-data", script, StringComparison.Ordinal);
        Assert.Contains("exec /usr/local/bin/garagebalance-reset-working-data", script, StringComparison.Ordinal);
        Assert.DoesNotContain("garagebalance-reset-working-data", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
