namespace GarageBalance.Api.Tests.Deployment;

public sealed class TestQualityGateTests
{
    [Fact]
    public void AgentInstructionsRequireTestsForChangedBehaviorAndBlockPublicationOnFailures()
    {
        var instructions = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "AGENTS.md"));

        Assert.Contains("## Mandatory Test Gate", instructions, StringComparison.Ordinal);
        Assert.Contains("Every new or changed method, function, endpoint, component, hook, query, filter, sort, pagination path, permission branch, validation rule, save/edit operation, error state, and performance-sensitive path", instructions, StringComparison.Ordinal);
        Assert.Contains("complete backend and frontend suites before committing or publishing", instructions, StringComparison.Ordinal);
        Assert.Contains("do not commit, merge, push or deploy while a required test, coverage threshold, build, lint, formatting, privacy or migration check is failing", instructions, StringComparison.Ordinal);
        Assert.Contains("GitHub Actions must execute the complete backend and frontend suites and enforce the configured coverage thresholds", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingWorkflowRunsCoverageGatesBeforePackagingAndDeployment()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy-staging.yml"));

        Assert.Contains("--collect:\"XPlat Code Coverage\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--settings backend-coverage.runsettings", workflow, StringComparison.Ordinal);
        Assert.Contains("./infrastructure/scripts/verify-backend-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:coverage", workflow, StringComparison.Ordinal);

        var backendGate = workflow.IndexOf("verify-backend-coverage.ps1", StringComparison.Ordinal);
        var frontendGate = workflow.IndexOf("npm run test:coverage", StringComparison.Ordinal);
        var packageStep = workflow.IndexOf("- name: Package release", StringComparison.Ordinal);
        var deployStep = workflow.IndexOf("- name: Apply release on VPS", StringComparison.Ordinal);

        Assert.True(backendGate >= 0 && backendGate < packageStep);
        Assert.True(frontendGate >= 0 && frontendGate < packageStep);
        Assert.True(packageStep < deployStep);
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
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не удалось найти корень репозитория.");
    }
}
