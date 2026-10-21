namespace GarageBalance.Api.Tests.Deployment;

public sealed class StagingRecordsCleanupDeploymentTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Workflow_ValidatesKindConfirmationAndFingerprintBeforeUsingTheProtectedVpsCommand()
    {
        var workflow = Read(".github", "workflows", "cleanup-staging-records.yml");

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: staging", workflow, StringComparison.Ordinal);
        Assert.Contains("- garage-history", workflow, StringComparison.Ordinal);
        Assert.Contains("- meter-history", workflow, StringComparison.Ordinal);
        Assert.Contains("- fund-balance", workflow, StringComparison.Ordinal);
        Assert.Contains("subject=\"ARCHIVED FUND BALANCE\"", workflow, StringComparison.Ordinal);
        Assert.Contains("action=\"REPAIR\"", workflow, StringComparison.Ordinal);
        Assert.Contains("subject=\"DELETED GARAGE HISTORY\"", workflow, StringComparison.Ordinal);
        Assert.Contains("subject=\"METER HISTORY\"", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$CONFIRMATION\" = \"PREVIEW GARAGEBALANCE ${subject}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$CONFIRMATION\" = \"${action} GARAGEBALANCE ${subject}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$FINGERPRINT\" =~ ^[0-9a-f]{64}$ ]]", workflow, StringComparison.Ordinal);
        Assert.True(workflow.IndexOf("Validate inputs", StringComparison.Ordinal) < workflow.IndexOf("Prepare SSH", StringComparison.Ordinal));
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply records '${KIND}' preview '${CONFIRMATION}'", workflow, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply records '${KIND}' cleanup '${CONFIRMATION}' '${FINGERPRINT}'", workflow, StringComparison.Ordinal);
        // Inputs reach the remote command only through validated environment variables.
        Assert.DoesNotContain("'${{ inputs.", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sudo /usr/local/bin/garagebalance-deploy-apply records '${{", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_PreviewsWithoutChangesAndCleansOnlyAVerifiedPlanAfterABackup()
    {
        var script = Read("infrastructure", "scripts", "cleanup-staging-records.sh");

        Assert.Contains("EXPECTED_DATABASE=\"garagebalance_staging\"", script, StringComparison.Ordinal);
        Assert.Contains("PLAN_COMMAND=\"archived-garages-purge-plan\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_COMMAND=\"archived-garages-purge\"", script, StringComparison.Ordinal);
        Assert.Contains("PLAN_COMMAND=\"meter-history-plan\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_COMMAND=\"meter-history-cleanup\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_CONFIRMATION=\"DELETE GARAGEBALANCE DELETED GARAGE HISTORY\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_CONFIRMATION=\"DELETE GARAGEBALANCE METER HISTORY\"", script, StringComparison.Ordinal);
        Assert.Contains("PLAN_COMMAND=\"archived-fund-balance-plan\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_COMMAND=\"archived-fund-balance-repair\"", script, StringComparison.Ordinal);
        Assert.Contains("CLEANUP_CONFIRMATION=\"REPAIR GARAGEBALANCE ARCHIVED FUND BALANCE\"", script, StringComparison.Ordinal);
        Assert.Contains("[[ \"$fingerprint\" =~ ^[0-9a-f]{64}$ ]]", script, StringComparison.Ordinal);
        Assert.Contains("-mmin \"-${PLAN_MAX_AGE_MINUTES}\"", script, StringComparison.Ordinal);

        // Order: plan lookup, service stop, backup, restore check, receipt, cleanup, start, health check.
        var lookup = script.IndexOf("no preview from the last 23 hours has this fingerprint", StringComparison.Ordinal);
        var stop = script.IndexOf("systemctl stop \"$SERVICE_NAME\"", StringComparison.Ordinal);
        var backup = script.IndexOf("pg_dump --format=custom", StringComparison.Ordinal);
        var restore = script.IndexOf("pg_restore --exit-on-error", StringComparison.Ordinal);
        var schema = script.IndexOf("restored schema does not match the previewed database", StringComparison.Ordinal);
        var receipt = script.IndexOf("'restoreSucceeded', true", StringComparison.Ordinal);
        var cleanup = script.IndexOf("\"$CLEANUP_COMMAND\" --execute", StringComparison.Ordinal);
        var start = script.IndexOf("systemctl start \"$SERVICE_NAME\"\nSERVICE_STOPPED=0", StringComparison.Ordinal);
        var health = script.IndexOf("health_check\n", start, StringComparison.Ordinal);
        Assert.True(lookup > 0 && lookup < stop && stop < backup && backup < restore && restore < schema && schema < receipt &&
            receipt < cleanup && cleanup < start && start < health);

        Assert.Contains("--confirm \"$fingerprint\"", script, StringComparison.Ordinal);
        Assert.Contains("--service \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("trap cleanup EXIT", script, StringComparison.Ordinal);
        Assert.Contains("dropdb --if-exists --force \"$RESTORE_CHECK_DATABASE\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeployPackage_InstallsTheScriptBehindTheExistingApplyCommand()
    {
        var workflow = Read(".github", "workflows", "deploy-staging.yml");
        var applyScript = Read("infrastructure", "scripts", "vps-apply-release.sh");

        Assert.Contains("infrastructure/scripts/cleanup-staging-records.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("if [[ \"${1:-}\" == \"records\" ]]; then", applyScript, StringComparison.Ordinal);
        Assert.Contains("exec /usr/local/bin/garagebalance-records-cleanup \"$@\"", applyScript, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-records-cleanup\n", applyScript, StringComparison.Ordinal);
        Assert.Contains("staging records cleanup script was not found", applyScript, StringComparison.Ordinal);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

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
