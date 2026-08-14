namespace GarageBalance.Api.Tests.Deployment;

public sealed class ShowcaseDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PreparationScript_BackupsRestoreChecksGuardsAndAuditsBeforeSuccess()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "infrastructure",
            "scripts",
            "prepare-staging-showcase.sh"));

        Assert.Contains("EXPECTED_CONFIRMATION=\"PREPARE GARAGEBALANCE STAGING\"", script, StringComparison.Ordinal);
        Assert.Contains("database_name\" == \"garagebalance_staging", script, StringComparison.Ordinal);
        Assert.Contains("pg_dump --format=custom", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore --exit-on-error", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore --clean --if-exists --exit-on-error", script, StringComparison.Ordinal);
        Assert.Contains("reason=archive-path", script, StringComparison.Ordinal);
        Assert.Contains("reason=archive-owner", script, StringComparison.Ordinal);
        Assert.Contains("--no-same-owner --no-same-permissions", script, StringComparison.Ordinal);
        Assert.Contains("Host=/var/run/postgresql;Database=${database_name};Username=postgres", script, StringComparison.Ordinal);
        Assert.Contains("sudo -u postgres env", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo -u \"$APP_USER\" env", script, StringComparison.Ordinal);
        Assert.Contains("GarageBalance.ShowcaseSeed\" prepare", script, StringComparison.Ordinal);
        Assert.Contains("GarageBalance.ShowcaseSeed\" audit", script, StringComparison.Ordinal);
        Assert.Contains("/health/ready", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualWorkflow_RequiresExactConfirmationAndUsesExistingAuthorizedDeployHelper()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            ".github",
            "workflows",
            "prepare-staging-showcase.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.confirmation == 'PREPARE GARAGEBALANCE STAGING'", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-deploy-apply prepare-showcase", workflow, StringComparison.Ordinal);
        Assert.Contains("concurrency:", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: staging", workflow, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru/health/ready", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RegularDeployment_InstallsShowcaseHelperButNeverRunsItAutomatically()
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

        Assert.Contains("infrastructure/scripts/prepare-staging-showcase.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-showcase-prepare", script, StringComparison.Ordinal);
        Assert.Contains("exec /usr/local/bin/garagebalance-showcase-prepare", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GarageBalance.ShowcaseSeed", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("garagebalance-showcase-prepare", workflow, StringComparison.Ordinal);
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
