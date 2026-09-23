namespace GarageBalance.Api.Tests.Deployment;

public sealed class StagingCloudBackupDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void StagingReleasePackagesAndInstallsRestrictedCloudConfigurator()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "deploy-staging.yml"));
        var apply = File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("infrastructure/scripts/configure-staging-cloud-backup.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("infrastructure/scripts/run-staging-storage-tool.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/storage-tool.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("storage-tool/GarageBalance.StorageTool", workflow, StringComparison.Ordinal);
        Assert.Contains("exec /usr/local/bin/garagebalance-configure-cloud-backup \"$@\"", apply, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-configure-cloud-backup", apply, StringComparison.Ordinal);
        Assert.Contains("STORAGE_TOOL_ARCHIVE=", apply, StringComparison.Ordinal);
        Assert.Contains("ln -sfn \"${RELEASE_DIR}/storage-tool\"", apply, StringComparison.Ordinal);
        Assert.Contains("bash -n \\", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudConfiguratorProtectsCredentialsAndRollsBackFailedStartup()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", "configure-staging-cloud-backup.sh"));

        Assert.Contains("IFS= read -r key_id", script, StringComparison.Ordinal);
        Assert.Contains("IFS= read -r key_secret", script, StringComparison.Ordinal);
        Assert.Contains("umask 077", script, StringComparison.Ordinal);
        Assert.Contains("chmod 600 \"$temporary_env\"", script, StringComparison.Ordinal);
        Assert.Contains("EnvironmentFile=%s", script, StringComparison.Ordinal);
        Assert.Contains("trap rollback ERR", script, StringComparison.Ordinal);
        Assert.Contains("Storage__Mode=AsyncMirror", script, StringComparison.Ordinal);
        Assert.Contains("Storage__Policies__0__RequiredIndependentCopies=2", script, StringComparison.Ordinal);
        Assert.Contains("Storage__Policies__0__MinimumOffsiteCopies=1", script, StringComparison.Ordinal);
        Assert.Contains("Storage__Destinations__1__EncryptionMode=SseKms", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance-storage-tool@.service", script, StringComparison.Ordinal);
        Assert.Contains("run)", script, StringComparison.Ordinal);
        Assert.Contains("ExecStart=/usr/local/bin/garagebalance-storage-tool-run %i", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Key Secret", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageRunnerUsesCheckpointOnlyForBackfillCommands()
    {
        var runner = File.ReadAllText(Path.Combine(RepositoryRoot, "infrastructure", "scripts", "run-staging-storage-tool.sh"));

        Assert.Contains("copy|resume|delta-sync)", runner, StringComparison.Ordinal);
        Assert.Contains("--execute --checkpoint \"$checkpoint\"", runner, StringComparison.Ordinal);
        Assert.Contains("inventory|plan|cutover-check|status)", runner, StringComparison.Ordinal);
        Assert.Contains("exec \"$tool\" \"$1\"", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("eval ", runner, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
