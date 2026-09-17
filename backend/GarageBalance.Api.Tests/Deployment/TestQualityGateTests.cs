namespace GarageBalance.Api.Tests.Deployment;

public sealed class TestQualityGateTests
{
    [Fact]
    public void AgentInstructionsRequireRiskBasedTestsAndBlockPublicationOnFailures()
    {
        var instructions = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AGENTS.md"));

        Assert.Contains("## Risk-Based Test Gate", instructions, StringComparison.Ordinal);
        Assert.Contains("Every new or changed method, function, endpoint, component behavior, hook, query, filter, sort, pagination path, permission branch, validation rule, save/edit operation, error state, and performance-sensitive path", instructions, StringComparison.Ordinal);
        Assert.Contains("selecting the smallest sufficient check during development and batching full suites at the publication boundary", instructions, StringComparison.Ordinal);
        Assert.Contains("For a pure presentation change, do not run backend tests, PostgreSQL, migrations, Docker, end-to-end suites, or the complete frontend suite locally", instructions, StringComparison.Ordinal);
        Assert.Contains("Run the project's complete applicable verification once after the intended batch of changes is finished and before push, pull request publication, release, or deployment", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not commit, merge, push, publish, or deploy while a check required for that boundary is failing", instructions, StringComparison.Ordinal);
        Assert.Contains("GitHub Actions must continue to execute the complete backend and frontend suites and enforce configured coverage, build, security/privacy, migration, and packaging/deployment gates", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingWorkflowRunsCoverageGatesBeforePackagingAndDeployment()
    {
        var workflow = File
            .ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy-staging.yml"))
            .ReplaceLineEndings("\n");

        Assert.Contains("--collect:\"XPlat Code Coverage\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--settings backend-coverage.runsettings", workflow, StringComparison.Ordinal);
        Assert.Contains("./infrastructure/scripts/verify-backend-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", workflow, StringComparison.Ordinal);
        Assert.Contains("needs:\n      - backend\n      - frontend\n      - backend-quality\n      - frontend-audit", workflow, StringComparison.Ordinal);

        var deployJob = workflow.IndexOf("  deploy:\n", StringComparison.Ordinal);
        var deployStep = workflow.IndexOf("- name: Apply release on VPS", StringComparison.Ordinal);

        Assert.True(deployJob >= 0 && deployJob < deployStep);
    }

    [Fact]
    public void BackendCoverageGateExcludesGeneratedMigrationsAndEnforcesStableThresholds()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settings = File.ReadAllText(Path.Combine(repositoryRoot, "backend-coverage.runsettings"));
        var verifier = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "verify-backend-coverage.ps1"));

        Assert.Contains("XPlat Code Coverage", settings, StringComparison.Ordinal);
        Assert.Contains("**/Infrastructure/Data/Migrations/*.cs", settings, StringComparison.Ordinal);
        Assert.Contains("**/Program.cs", settings, StringComparison.Ordinal);
        Assert.Contains("[double]$MinimumLineRate = 85", verifier, StringComparison.Ordinal);
        Assert.Contains("[double]$MinimumBranchRate = 70", verifier, StringComparison.Ordinal);
        Assert.Contains("coverage.cobertura.xml", verifier, StringComparison.Ordinal);
        Assert.Contains("throw", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendCoverageGateTracksStatementsBranchesFunctionsAndLines()
    {
        var repositoryRoot = FindRepositoryRoot();
        var package = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "package.json"));
        var config = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "vitest.config.ts"));

        Assert.Contains("\"test:coverage\": \"node scripts/run-vitest.mjs --ci --coverage.enabled=true\"", package, StringComparison.Ordinal);
        Assert.Contains("\"@vitest/coverage-v8\"", package, StringComparison.Ordinal);
        Assert.Contains("provider: 'v8'", config, StringComparison.Ordinal);
        Assert.Contains("statements: 78", config, StringComparison.Ordinal);
        Assert.Contains("branches: 69", config, StringComparison.Ordinal);
        Assert.Contains("functions: 74", config, StringComparison.Ordinal);
        Assert.Contains("lines: 79", config, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                 File.Exists(Path.Combine(directory.FullName, ".git"))))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти корень репозитория.");
    }
}
