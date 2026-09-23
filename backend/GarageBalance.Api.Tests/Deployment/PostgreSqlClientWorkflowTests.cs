namespace GarageBalance.Api.Tests.Deployment;

public sealed class PostgreSqlClientWorkflowTests
{
    [Theory]
    [InlineData("verify.yml")]
    [InlineData("deploy-staging.yml")]
    [InlineData("publish-docker-release.yml")]
    public void FullPostgreSqlSuitesPrepareMatchingClientToolsBeforeTests(string fileName)
    {
        var workflow = File.ReadAllText(Path.Combine(BranchVerificationTests.FindRepositoryRoot(), ".github", "workflows", fileName));
        var setup = workflow.IndexOf("run: bash infrastructure/scripts/setup-postgresql-client-ci.sh", StringComparison.Ordinal);
        var tests = workflow.IndexOf("dotnet test GarageBalance.slnx", StringComparison.Ordinal);
        Assert.Contains("image: postgres:17", workflow, StringComparison.Ordinal);
        Assert.Contains("GARAGEBALANCE_POSTGRES_TEST_CONNECTION:", workflow, StringComparison.Ordinal);
        Assert.True(setup >= 0 && tests > setup, "PostgreSQL 17 client setup must precede the complete database suite.");
    }

    [Fact]
    public void ClientSetupIsRunnerOnlyUsesOfficialSignedRepositoryAndDoesNotInstallServer()
    {
        var script = File.ReadAllText(Path.Combine(BranchVerificationTests.FindRepositoryRoot(), "infrastructure", "scripts", "setup-postgresql-client-ci.sh"));
        Assert.Contains("set -euo pipefail", script, StringComparison.Ordinal);
        Assert.Contains("${GITHUB_ACTIONS:-}", script, StringComparison.Ordinal);
        Assert.Contains("${ID:-}", script, StringComparison.Ordinal);
        Assert.Contains("postgres_bin=/usr/lib/postgresql/17/bin", script, StringComparison.Ordinal);
        Assert.Contains("! -x \"$postgres_bin/pg_dump\" || ! -x \"$postgres_bin/pg_restore\"", script, StringComparison.Ordinal);
        Assert.Contains("https://apt.postgresql.org/pub/repos/apt", script, StringComparison.Ordinal);
        Assert.Contains("sha256sum --check --strict", script, StringComparison.Ordinal);
        Assert.Contains("Signed-By:", script, StringComparison.Ordinal);
        Assert.Contains("apt-get install --yes --no-install-recommends postgresql-client-17", script, StringComparison.Ordinal);
        Assert.DoesNotContain("apt-get install --yes --no-install-recommends postgresql-17", script, StringComparison.Ordinal);
        Assert.Contains("for tool in pg_dump pg_restore", script, StringComparison.Ordinal);
        Assert.Contains("--version", script, StringComparison.Ordinal);
        Assert.Contains(">> \"$GITHUB_PATH\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--allow-unauthenticated", script, StringComparison.Ordinal);
        Assert.DoesNotContain("apt-key", script, StringComparison.Ordinal);
    }
}
