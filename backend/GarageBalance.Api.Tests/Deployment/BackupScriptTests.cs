namespace GarageBalance.Api.Tests.Deployment;

public sealed class BackupScriptTests
{
    [Fact]
    public void BackupScriptCreatesCustomFormatDumpAndVerifiesNonEmptyFile()
    {
        var script = ReadRepositoryFile("infrastructure", "scripts", "backup-postgres.ps1");

        Assert.Contains("[CmdletBinding()]", script, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Backups", script, StringComparison.Ordinal);
        Assert.Contains("Get-Command pg_dump", script, StringComparison.Ordinal);
        Assert.Contains("--format=custom", script, StringComparison.Ordinal);
        Assert.Contains(".pgdump", script, StringComparison.Ordinal);
        Assert.Contains("Backup file is empty.", script, StringComparison.Ordinal);
        Assert.Contains("backupPath=", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreScriptDefaultsToCheckDatabaseAndProtectsProductionTargets()
    {
        var script = ReadRepositoryFile("infrastructure", "scripts", "restore-postgres.ps1");

        Assert.Contains("SupportsShouldProcess = $true", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", script, StringComparison.Ordinal);
        Assert.Contains("Get-Command pg_restore", script, StringComparison.Ordinal);
        Assert.Contains("Get-Command psql", script, StringComparison.Ordinal);
        Assert.Contains("AllowProductionTarget", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_local", script, StringComparison.Ordinal);
        Assert.Contains("--no-owner", script, StringComparison.Ordinal);
        Assert.Contains("--no-privileges", script, StringComparison.Ordinal);
        Assert.Contains("restoreDatabase=", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ScheduledTaskScriptRegistersDailyLocalBackup()
    {
        var script = ReadRepositoryFile("infrastructure", "scripts", "register-local-backup-task.ps1");

        Assert.Contains("GarageBalance Local PostgreSQL Backup", script, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", script, StringComparison.Ordinal);
        Assert.Contains("C:\\GarageBalance\\Backups", script, StringComparison.Ordinal);
        Assert.Contains("New-ScheduledTaskTrigger -Daily", script, StringComparison.Ordinal);
        Assert.Contains("Register-ScheduledTask", script, StringComparison.Ordinal);
        Assert.Contains("scheduledTask=", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalPostgresPreflightChecksTcpPsqlAndAvoidsPrintingSecrets()
    {
        var script = ReadRepositoryFile("infrastructure", "scripts", "check-local-postgres.ps1");

        Assert.Contains("[CmdletBinding()]", script, StringComparison.Ordinal);
        Assert.Contains("Test-NetConnection", script, StringComparison.Ordinal);
        Assert.Contains("Get-Command psql", script, StringComparison.Ordinal);
        Assert.Contains("SELECT 1;", script, StringComparison.Ordinal);
        Assert.Contains("connectionStringProvided=", script, StringComparison.Ordinal);
        Assert.Contains("postgresTcp=", script, StringComparison.Ordinal);
        Assert.Contains("psqlConnection=", script, StringComparison.Ordinal);
        Assert.Contains("localPostgresPreflight=OK", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output $ConnectionString", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupDocumentationReferencesScriptsRestoreCheckAndMonthlyValidation()
    {
        var document = ReadRepositoryFile("docs", "postgres-backup-restore.md");

        Assert.Contains("check-local-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("backup-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("restore-postgres.ps1", document, StringComparison.Ordinal);
        Assert.Contains("register-local-backup-task.ps1", document, StringComparison.Ordinal);
        Assert.Contains("pg_dump --format=custom", document, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", document, StringComparison.Ordinal);
        Assert.Contains("GarageBalance Local PostgreSQL Backup", document, StringComparison.Ordinal);
        Assert.Contains("Раз в месяц выполнять restore-check", document, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        return File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. pathParts]));
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
