namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgresHealthDiagnosticsTests
{
    [Fact]
    public void HealthSql_CoversMaintenanceCacheAndBlockingWithoutReadingQueryText()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sql = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "infrastructure",
            "postgres",
            "postgres-health.sql"));

        Assert.Contains("pg_stat_database", sql, StringComparison.Ordinal);
        Assert.Contains("pg_stat_user_tables", sql, StringComparison.Ordinal);
        Assert.Contains("pg_stat_activity", sql, StringComparison.Ordinal);
        Assert.Contains("autovacuum_vacuum_threshold", sql, StringComparison.Ordinal);
        Assert.Contains("autovacuum_analyze_scale_factor", sql, StringComparison.Ordinal);
        Assert.Contains("waiting_locks", sql, StringComparison.Ordinal);
        Assert.Contains("transactions_over_30_seconds", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("query AS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT query", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HealthScript_UsesPsqlEnvironmentAndDoesNotEmbedCredentials()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "infrastructure",
            "scripts",
            "check-postgres-health.ps1"));

        Assert.Contains("$env:PGDATABASE", script, StringComparison.Ordinal);
        Assert.Contains("ON_ERROR_STOP=1", script, StringComparison.Ordinal);
        Assert.Contains("postgres-health.sql", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PGPASSWORD=", script, StringComparison.OrdinalIgnoreCase);
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
