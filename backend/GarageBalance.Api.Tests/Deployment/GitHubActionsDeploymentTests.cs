namespace GarageBalance.Api.Tests.Deployment;

public sealed class GitHubActionsDeploymentTests
{
    [Fact]
    public void StagingWorkflowVerifiesBuildsPackagesAndDeploysThroughRestrictedServerScript()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy-staging.yml"));

        Assert.Contains("branches:", workflow, StringComparison.Ordinal);
        Assert.Contains("- master", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --configuration Release --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format GarageBalance.slnx --verify-no-changes --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("./infrastructure/scripts/verify-package-privacy.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run lint", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run build", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/api.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/frontend.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.VPS_SSH_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("Host garagebalance-staging", workflow, StringComparison.Ordinal);
        Assert.Contains("ControlMaster auto", workflow, StringComparison.Ordinal);
        Assert.Contains("ControlPersist 120", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging:~/uploads/${RELEASE_ID}/api.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("ssh -O exit garagebalance-staging", workflow, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void VpsApplyReleaseScriptCreatesBackupAppliesMigrationsChecksHealthAndKeepsRollback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("pg_dump --format=custom", script, StringComparison.Ordinal);
        Assert.Contains("psql --set ON_ERROR_STOP=1", script, StringComparison.Ordinal);
        Assert.Contains("systemctl stop \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("systemctl start \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("restore_previous_release", script, StringComparison.Ordinal);
        Assert.Contains("curl -fsS -H \"Host: ${PUBLIC_HOST}\"", script, StringComparison.Ordinal);
        Assert.Contains("curl -fsSk -H \"Host: ${PUBLIC_HOST}\" \"https://127.0.0.1/health\"", script, StringComparison.Ordinal);
        Assert.Contains("deployStatus=ok", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_${TIMESTAMP}_${release_id}.pgdump", script, StringComparison.Ordinal);
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
