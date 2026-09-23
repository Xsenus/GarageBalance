using GarageBalance.StorageTool;

namespace GarageBalance.Api.Tests.Deployment;

public sealed class StorageToolTests
{
    [Fact]
    public void MigrationCommands_DefaultToDryRun_AndDeletionIsNotPublicSurface()
    {
        foreach (var command in new[] { "inventory", "plan", "copy", "verify", "diff", "delta-sync", "resume", "repair", "status", "report", "cutover-check", "rollback-check" })
        {
            var options = MigrationCommandOptions.Parse([command, "--checkpoint", "private-checkpoint.json"]);
            Assert.Equal(command, options.Command);
            Assert.False(options.Execute);
        }
        Assert.Throws<MigrationToolException>(() => MigrationCommandOptions.Parse(["delete", "--execute"]));
    }

    [Fact]
    public void OperationsGuideDocumentsMigrationRestoreRecoveryAndRollback()
    {
        var document = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "storage-operations.md"));
        Assert.Contains("inventory` → `plan` → `copy` → `verify`", document, StringComparison.Ordinal);
        Assert.Contains("cutover-check", document, StringComparison.Ordinal);
        Assert.Contains("rollback-check", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("recovery-bundle.ps1", document, StringComparison.Ordinal);
        Assert.Contains("RPO/RTO", document, StringComparison.Ordinal);
        Assert.Contains("два действительно независимых назначения", document, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
