namespace GarageBalance.Api.Tests.Deployment;

public sealed class StorageToolTests
{
    [Fact]
    public void MigrationToolIsDryRunGuardedResumableAndHasNoSourceDeleteCommand()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.StorageTool", "Program.cs"));

        foreach (var command in new[] { "inventory", "plan", "copy", "verify", "diff", "delta-sync", "resume", "repair", "status", "report", "cutover-check", "rollback-check" })
        {
            Assert.Contains($"\"{command}\"", source, StringComparison.Ordinal);
        }
        Assert.Contains("--execute", source, StringComparison.Ordinal);
        Assert.Contains("dry-run by default", source, StringComparison.Ordinal);
        Assert.Contains("RegisterVerifiedLocalFilesAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProcessNextAsync", source, StringComparison.Ordinal);
        Assert.Contains("insufficient > 0 ? 10 : 0", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", source, StringComparison.Ordinal);
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
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
